using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System;
using Cysharp.Threading.Tasks;


/// <summary>
/// ミニゲームの基盤。流れを進めて、状態を全員に同期する。各ミニゲームはこれを継承して作る
/// NetworkBehaviour : ネットワークの機能(NetworkVariable,RPC)が使える
/// </summary>
public abstract class MinigameBase : NetworkBehaviour
{
    [Header("流れ")]
    [Tooltip("カウントダウンの秒数。3なら「3,2,1,スタート！」。0の場合、スタートの合図のみ")]
    [SerializeField] private int countdownSeconds = 3;

    [Header("演出(待つ秒数。0 = 待たない)")]
    [Tooltip("イントロ演出の秒数(最初の1回だけ)。秒数で決まらない演出は OnServerIntroAsync を override する")]
    [SerializeField] private float introSeconds = 0f;

    [Tooltip("ラウンド開始演出の秒数(毎ラウンド、カウントダウンの前)。例: 目標を見せる。秒数で決まらない演出は OnServerRoundIntroAsync を override する")]
    [SerializeField] private float roundIntroSeconds = 0f;

    [Tooltip("ラウンド終了演出の秒数(毎ラウンド、終わった直後)。例: フィニッシュ！・結果を見せる。秒数で決まらない演出は OnServerRoundEndAsync を override する")]
    [SerializeField] private float roundEndSeconds = 0f;

    [Tooltip("ゲーム終了演出の秒数(最後のラウンドの後、順位の前)。例: 全員の結果を一斉に見せる。秒数で決まらない演出は OnServerGameEndAsync を override する")]
    [SerializeField] private float gameEndSeconds = 0f;

    [Header("ラウンド")]
    [Tooltip("ラウンド数。1 = ラウンド制ではない")]
    [SerializeField] private int totalRounds = 1;

    [Tooltip("1ラウンドの制限時間(秒)。0 = 制限なし")]
    [SerializeField] private float roundTimeLimit = 30.0f;

    [Tooltip("ラウンドごとの値を、最終的な値にどうまとめるか。ラウンド数が1のときは使わない")]
    [SerializeField] private MinigameRoundAggregation roundAggregation = MinigameRoundAggregation.Sum;

    [Tooltip("まとめ方が RankPoints のときの、順位ごとの勝ち点(1位, 2位, 3位, 4位 の順)")]
    [SerializeField] private int[] rankPoints = { 3, 2, 1, 0 };

    [Header("操作")]
    [Tooltip("オン = このラウンドを終えた人(ServerSetFinished された人)の行動は受け付けない。1ラウンドに1回だけ押すゲーム向け(例: ストップウォッチ)。何度でも押すゲームで ServerSetFinished を使わないなら、オンのままでも影響はない")]
    [SerializeField] private bool ignoreActionsAfterFinished = true;

    [Header("結果")]
    [Tooltip("値が大きいほど勝ちか、小さいほど勝ちか。まとめ方が RankPoints のときは、各ラウンドの順位を決めるのに使う")]
    [SerializeField] private MinigameWinOrder winOrder = MinigameWinOrder.HigherIsBetter;

    [Tooltip("値の種類(得点 / 時間 / 回数)。UI が表示を切り替えるのに使える")]
    [SerializeField] private MinigameValueKind valueKind = MinigameValueKind.Score;

    [Tooltip("値を表示するときの形式。\"0\" = 整数、\"0.00\" = 小数第2位まで")]
    [SerializeField] private string valueFormat = "0";

    [Tooltip("値の後ろに付ける単位。例: 点、秒、回")]
    [SerializeField] private string valueUnit = "点";

    [Header("リセット")]
    [Tooltip("次のラウンドの前に、キャラクターを初期位置に戻すときに使う。空欄ならシーンから自動で探す(見つからなければ位置は戻さない)")]
    [SerializeField] private PlayerSpawnPoints spawnPoints;

    [Tooltip("次のラウンドの前に位置を戻す間、全員の画面を暗くする。オフなら暗くせずにその場で戻す")]
    [SerializeField] private bool fadeOnRoundReset = true;

    [Tooltip("最後のラウンドの後も、ゲーム終了演出の前に暗くして明るくする(暗い間にフェーズが GameEnd になる)")]
    [ShowIf(nameof(fadeOnRoundReset))]
    [SerializeField] private bool fadeAfterLastRound = true;

    [Tooltip("オン = 少しずつ暗くする。オフ = 一瞬で暗くする")]
    [ShowIf(nameof(fadeOnRoundReset))]
    [SerializeField] private bool resetFadeOut = true;

    [Tooltip("暗くなるまでの秒数")]
    [ShowIf(nameof(fadeOnRoundReset), nameof(resetFadeOut))]
    [SerializeField] private float resetFadeOutSeconds = 0.3f;

    [Tooltip("オン = 少しずつ明るくする。オフ = 一瞬で明るくする")]
    [ShowIf(nameof(fadeOnRoundReset))]
    [SerializeField] private bool resetFadeIn = true;

    [Tooltip("明るくなるまでの秒数")]
    [ShowIf(nameof(fadeOnRoundReset), nameof(resetFadeIn))]
    [SerializeField] private float resetFadeInSeconds = 0.3f;

    // 位置を戻してから明るくするまでに待つ秒数。
    // キャラクターの位置は所有者の端末が動かして全員に配るので、移動が全員の画面に届くのを少し待つ
    private const float ResetMoveSettleSeconds = 0.5f;


    // 今のフェーズ。サーバーだけが書き換え、全員が値を読める
    private readonly NetworkVariable<MinigamePhase> phase = new NetworkVariable<MinigamePhase>(MinigamePhase.WaitingForPlayers);

    // ラウンドが終わるサーバーの時刻。-1 = 制限なし(またはプレイ中ではない)
    // サーバーの時間のため、floatではなくdoubleにする
    private readonly NetworkVariable<double> roundEndServerTime = new NetworkVariable<double>(-1);

    // 今のラウンド(1から数える)。0 = まだ始まっていない
    private readonly NetworkVariable<int> currentRound = new NetworkVariable<int>(0);

    // 全員分のプレイヤー情報。サーバーだけが書き換え、全員に同期される
    private readonly NetworkList<MinigamePlayerState> players = new NetworkList<MinigamePlayerState>();

    // 独自の情報(名前と値の組)。サーバーだけが書き換え、全員に同期される。例: 「目標時間」= 10
    private readonly NetworkList<MinigameCustomInfo> customInfos = new NetworkList<MinigameCustomInfo>();

    // 今のラウンドのプレイが始まったサーバー時刻。-1 = まだ始まっていない。サーバーだけが書き換え、全員に同期される
    // 全員の画面で経過時間をそろえるのと、行動の「押した時刻」の確認に使う
    private readonly NetworkVariable<double> roundStartServerTime = new NetworkVariable<double>(-1);

    // 「準備ができた」が届いた人のID(サーバーだけが使う)
    private readonly HashSet<ulong> readyClientIds = new HashSet<ulong>();

    // このオブジェクトに付いている終了条件(サーバーだけが使う)。どれか1つを満たすとラウンドが終わる
    private MinigameEndCondition[] endConditions;

    // 枠番号ごとの、各ラウンドの値の記録(サーバーだけが使う)。まとめ方が合計以外のときに、合計の値を計算し直すのに使う
    private readonly Dictionary<int, List<float>> roundValueHistory = new Dictionary<int, List<float>>();

    // 進行役(ServerRunGameAsync)を止めるための合図(サーバーだけが使う)。
    // 通信が止まったとき(OnNetworkDespawn)に止める。オブジェクトが消えるのを待つと、その間に合図(RPC)を送ろうとしてエラーになるため
    private CancellationTokenSource gameCts;



    // 準備ができたことをつたえる
    [Rpc(SendTo.Server)] // サーバーの端末で中身が実行
    private void NotifyReadyRpc(RpcParams rpcParams = default)　// 誰が送ってきたかの情報、RPCのメソッド名はRpcで終わる必要がある
    {
        readyClientIds.Add(rpcParams.Receive.SenderClientId);
        ServerCheckAllReady();
    }

    // 全員の準備できたがそろったか調べる(サーバーで呼ばれる)。揃ったらカウントダウンへ進む。
    private void ServerCheckAllReady()
    {
        if(Phase != MinigamePhase.WaitingForPlayers)
        {
            return;
        }

        // 接続中の全員を一人ずつ調べる
        foreach(ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!readyClientIds.Contains(clientId))
            {
                return;
            }
        }
        Debug.Log("全員揃いました");
        ServerRunGameAsync().Forget();
    }


    // ゲーム全体の進行役(サーバーで呼ばれる)。流れを上から順に、ラウンドの回数だけ繰り返す
    // 各フェーズの演出は OnServer〇〇Async で待つ(何も書かなければ、インスペクターの秒数だけ待つ)
    private async UniTaskVoid ServerRunGameAsync()
    {
        // オブジェクトが消えたとき・通信が止まったときの、どちらでも止まるようにする
        gameCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        CancellationToken ct = gameCts.Token;

        // 担当者のONServerGameSetupより先に作る
        ServerSetupPlayers();
        customInfos.Clear(); // 前のゲームの独自の情報を残さない
        OnServerGameSetup();

        // シーン遷移のフェードインが終わってから始める(画面が暗いうちにイントロやカウントダウンが進まないように)
        await ServerWaitForSceneTransitionAsync(ct);

        // イントロ演出(最初の1回だけ)
        ServerSetPhase(MinigamePhase.Intro);
        await OnServerIntroAsync(ct);

        for (int round = 1; round <= totalRounds; round++)
        {
            currentRound.Value = round;
            ServerResetRoundValues();

            // ラウンド開始演出(例: 目標を見せる)
            ServerSetPhase(MinigamePhase.RoundIntro);
            await OnServerRoundIntroAsync(round, ct);

            await ServerRunCountdownAsync(ct);
            RoundStartedRpc(round);
            OnServerRoundStart(round);

            // 時間切れ・終了条件・担当者の ServerEndRound() のどれかで Playing でなくなるまで待つ
            await UniTask.WaitUntil(() => Phase != MinigamePhase.Playing, cancellationToken: ct);
            RoundEndedRpc(round);
            OnServerRoundEnd(round);

            // 担当者が OnServerRoundEnd で値を確定させた後に、このラウンドの値を記録して合計を出し直す
            ServerRecordRoundValues();

            // ラウンド終了演出(例: フィニッシュ！・止めた時間を見せる)。フェーズは ServerEndRound で RoundEnd になっている
            await OnServerRoundEndAsync(round, ct);
            roundEndServerTime.Value = -1;

            if (round < totalRounds)
            {
                // 最後のラウンドでなければ、次のラウンドの準備をする(暗くする → 位置を戻す → 明るくする)
                await ServerFadeOutAsync(ct);
                ServerSetPhase(MinigamePhase.RoundReset);
                ServerResetForNextRound(round + 1);
                await ServerFadeInAsync(ct);
            }
            else if (fadeOnRoundReset && fadeAfterLastRound)
            {
                // 最後のラウンドの後: 暗くしている間に GameEnd にして、明るくしてからゲーム終了演出
                await ServerFadeOutAsync(ct);
                ServerSetPhase(MinigamePhase.GameEnd);
                await ServerFadeInAsync(ct);
            }
        }

        // ゲーム終了演出(例: 全員の結果を一斉に見せる)
        ServerSetPhase(MinigamePhase.GameEnd); // 暗くする設定のときは、もう GameEnd になっている(同じ値なら何も起きない)
        await OnServerGameEndAsync(ct);

        // 順位を計算して全員に送る
        List<MinigameRankEntry> ranking = CalculateRanking();
        ResultRpc(ranking.ToArray());
        ServerSetPhase(MinigamePhase.Result);
    }

    // シーン遷移(NetworkSceneTransition)のフェードインが終わるまで待つ(サーバーで呼ばれる)
    // シーンを単体で再生しているときなど、遷移の仕組みがなければすぐに進む
    private async UniTask ServerWaitForSceneTransitionAsync(CancellationToken ct)
    {
        NetworkSceneTransition transition = NetworkSceneTransition.Instance;
        if (transition == null) { return; }

        await UniTask.WaitUntil(() => !transition.IsTransitioning, cancellationToken: ct);
    }

    // 次のラウンドの準備(サーバーで呼ばれる)。値とラウンド数は引き継ぎ、キャラクターの位置などを戻す
    private void ServerResetForNextRound(int nextRound)
    {
        if (spawnPoints == null)
        {
            spawnPoints = FindFirstObjectByType<PlayerSpawnPoints>();
        }
        if (spawnPoints != null)
        {
            spawnPoints.PlaceAllPlayers();
        }

        OnServerRoundReset(nextRound);
    }

    // 全員の画面を暗くして、暗くなるまで待つ(サーバーで呼ばれる)。設定でフェードがオフなら何もしない
    private async UniTask ServerFadeOutAsync(CancellationToken ct)
    {
        if (!fadeOnRoundReset) { return; }

        float seconds = resetFadeOut ? resetFadeOutSeconds : 0f; // フェードアウトがオフなら一瞬で暗くする
        FadeOutRpc(seconds);
        await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct);
    }

    // 暗いまま少し待ってから全員の画面を明るくし、明るくなるまで待つ(サーバーで呼ばれる)。設定でフェードがオフなら何もしない
    private async UniTask ServerFadeInAsync(CancellationToken ct)
    {
        if (!fadeOnRoundReset) { return; }

        // キャラクターの位置は所有者の端末が動かして全員に配るので、移動が全員の画面に届くのを待ってから明るくする
        await UniTask.Delay(TimeSpan.FromSeconds(ResetMoveSettleSeconds), cancellationToken: ct);

        float seconds = resetFadeIn ? resetFadeInSeconds : 0f; // フェードインがオフなら一瞬で明るくする
        FadeInRpc(seconds);
        await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct);
    }

    // 全員の画面を暗くする(全員の端末で実行)。フェードの処理は既存の SceneLoader に任せる
    [Rpc(SendTo.Everyone)]
    private void FadeOutRpc(float seconds)
    {
        if (SceneLoader.Instance == null) { return; }
        SceneLoader.Instance.FadeOutAsync(seconds, destroyCancellationToken).Forget();
    }

    // 全員の画面を明るくする(全員の端末で実行)
    [Rpc(SendTo.Everyone)]
    private void FadeInRpc(float seconds)
    {
        if (SceneLoader.Instance == null) { return; }
        SceneLoader.Instance.FadeInAsync(seconds, destroyCancellationToken).Forget();
    }

    // 全員分のプレイヤー情報を作る(サーバーで、全員揃った直後に1回だけ呼ばれる)
    // シーンにいるキャラクタ(PlayerSlot)を探して、枠番号の順(1P, 2P, …)にリストに入れる
    private void ServerSetupPlayers()
    {
        players.Clear();
        roundValueHistory.Clear();

        List<PlayerSlot> slots = new List<PlayerSlot>(FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None));
        slots.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

        foreach (PlayerSlot slot in slots)
        {
            players.Add(new MinigamePlayerState
            {
                ClientId = slot.OwnerClientId,  // そのキャラクターを操作している端末のID
                SlotIndex = slot.SlotIndex,     // 枠番号(0 = 1P)
                IsConnected = true
                // それ以外(値・終えたか・脱落・CPU)は自動で 0 / false になる
            });
        }
    }

    // ラウンドの始めに、全員の「今回のラウンドの値」と「終えたか」を戻す(サーバーで呼ばれる)
    // 合計の値・脱落・接続中は引き継ぐ
    private void ServerResetRoundValues()
    {
        for (int i = 0; i < players.Count; i++)
        {
            MinigamePlayerState state = players[i];
            state.RoundValue = 0f;
            state.IsFinished = false;
            players[i] = state;
        }
    }

    // 枠番号(0 = 1P)から、リストの何番目かを探す。見つからなければ -1
    private int FindPlayerIndex(int slot)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].SlotIndex == slot) { return i; }
        }
        return -1;
    }

    // 担当者が呼ぶメソッドの入口チェック。サーバーでない・その枠の人がいない場合は警告を出して -1 を返す
    // Server で始まるメソッドの最初で呼んで、同じチェックを何度も書かずに済ませる
    private int ServerFindPlayerIndexChecked(int slot, string methodName)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"[MinigameBase] {methodName} はサーバーでしか呼べません");
            return -1;
        }

        int index = FindPlayerIndex(slot);
        if (index < 0)
        {
            Debug.LogWarning($"[MinigameBase] {methodName}: {slot + 1}P が見つかりません");
        }
        return index;
    }


    // ---------- 順位と結果(ステップ8) ----------

    // 合計の値を、値が変わるたびにその場で増やすか。
    // ラウンド制でない(1ラウンド)か、まとめ方が「合計」のときだけ。それ以外はラウンドの終わりにまとめて計算する
    private bool UsesLiveSum => totalRounds <= 1 || roundAggregation == MinigameRoundAggregation.Sum;

    // ラウンドの終わりに、このラウンドの値を記録して、まとめ方に合わせて合計の値を出し直す(サーバーで呼ばれる)
    private void ServerRecordRoundValues()
    {
        for (int i = 0; i < players.Count; i++)
        {
            MinigamePlayerState player = players[i];
            if (!roundValueHistory.TryGetValue(player.SlotIndex, out List<float> history))
            {
                history = new List<float>();
                roundValueHistory[player.SlotIndex] = history;
            }
            history.Add(player.RoundValue);
        }

        if (UsesLiveSum) { return; } // 合計はプレイ中にもう足してある

        // 勝ち点のときは、このラウンドの順位(今回の値で比べる)から勝ち点を出す
        Dictionary<int, int> roundRanks = null;
        if (roundAggregation == MinigameRoundAggregation.RankPoints)
        {
            roundRanks = new Dictionary<int, int>();
            foreach (MinigameRankEntry entry in RankByValue(useRoundValue: true, higherIsBetter: winOrder == MinigameWinOrder.HigherIsBetter))
            {
                roundRanks[entry.SlotIndex] = entry.Rank;
            }
        }

        for (int i = 0; i < players.Count; i++)
        {
            MinigamePlayerState state = players[i];
            List<float> history = roundValueHistory[state.SlotIndex];

            switch (roundAggregation)
            {
                case MinigameRoundAggregation.Best:
                    state.TotalValue = BestOf(history);
                    break;
                case MinigameRoundAggregation.Last:
                    state.TotalValue = history[history.Count - 1];
                    break;
                case MinigameRoundAggregation.RankPoints:
                    // 抜けた人など、このラウンドの順位がない人は0点
                    if (roundRanks.TryGetValue(state.SlotIndex, out int rank) && rank - 1 < rankPoints.Length)
                    {
                        state.TotalValue += rankPoints[rank - 1];
                    }
                    break;
            }
            players[i] = state;
        }
    }

    // 記録の中で一番よい値(勝ち方の設定に従う)
    private float BestOf(List<float> history)
    {
        float best = history[0];
        foreach (float value in history)
        {
            bool better = winOrder == MinigameWinOrder.HigherIsBetter ? value > best : value < best;
            if (better) { best = value; }
        }
        return best;
    }

    /// <summary>
    /// 最終的な順位を計算する(サーバーで、ゲームの最後に1回呼ばれる)。
    /// 標準では、接続中の人を「合計の値」と勝ち方の設定で並べる(同じ値は同じ順位)。
    /// 「脱落した順」など、値から順位が決まらないミニゲームは、これを override して独自に決める
    /// </summary>
    protected virtual List<MinigameRankEntry> CalculateRanking()
    {
        // 勝ち点でまとめたときは、勝ち点が大きいほど勝ち
        bool higherIsBetter = (roundAggregation == MinigameRoundAggregation.RankPoints && totalRounds > 1)
            || winOrder == MinigameWinOrder.HigherIsBetter;

        return RankByValue(useRoundValue: false, higherIsBetter: higherIsBetter);
    }

    /// <summary>
    /// 接続中の人を値で並べて順位を付ける(同じ値は同じ順位。例: 1位, 1位, 3位)。
    /// CalculateRanking を override するときにも使える
    /// </summary>
    /// <param name="useRoundValue">true = 今回のラウンドの値、false = 合計の値で比べる</param>
    /// <param name="higherIsBetter">true = 大きいほど上の順位</param>
    protected List<MinigameRankEntry> RankByValue(bool useRoundValue, bool higherIsBetter)
    {
        List<MinigameRankEntry> entries = new List<MinigameRankEntry>();
        for (int i = 0; i < players.Count; i++)
        {
            MinigamePlayerState player = players[i];
            if (!player.IsConnected) { continue; } // 途中で抜けた人は結果から外す

            entries.Add(new MinigameRankEntry
            {
                ClientId = player.ClientId,
                SlotIndex = player.SlotIndex,
                Value = useRoundValue ? player.RoundValue : player.TotalValue,
            });
        }

        // よい順に並べる。同じ値なら枠番号の順
        entries.Sort((a, b) =>
        {
            int byValue = higherIsBetter ? b.Value.CompareTo(a.Value) : a.Value.CompareTo(b.Value);
            return byValue != 0 ? byValue : a.SlotIndex.CompareTo(b.SlotIndex);
        });

        // 順位を付ける。前の人と同じ値なら同じ順位、違えば「自分の位置 + 1」位
        for (int i = 0; i < entries.Count; i++)
        {
            MinigameRankEntry entry = entries[i];
            entry.Rank = (i > 0 && entries[i].Value == entries[i - 1].Value) ? entries[i - 1].Rank : i + 1;
            entries[i] = entry;
        }
        return entries;
    }

    // 結果を全員に送る(全員の端末で実行)。受け取った結果は MinigameInfo.LastResult に残る
    [Rpc(SendTo.Everyone)]
    private void ResultRpc(MinigameRankEntry[] result)
    {
        foreach (MinigameRankEntry entry in result)
        {
            Debug.Log($"[MinigameBase] 結果: {entry.Rank}位 {entry.SlotIndex + 1}P 値 {entry.Value}");
        }
        MinigameInfo.SetResultAndRaise(result);
    }


    // ---------- UI に知らせる出来事(全員の端末で実行し、MinigameInfo のイベントを鳴らす) ----------
    // フェーズや値の同期(NetworkVariable / NetworkList)は一瞬の変化を飛ばすことがあるので、
    // 「起きたこと」を確実に知らせたいものは RPC で送る

    [Rpc(SendTo.Everyone)]
    private void RoundStartedRpc(int round) => MinigameInfo.RaiseRoundStarted(round);

    [Rpc(SendTo.Everyone)]
    private void RoundEndedRpc(int round) => MinigameInfo.RaiseRoundEnded(round);

    [Rpc(SendTo.Everyone)]
    private void PlayerValueChangedRpc(int slot, float delta, float newValue) => MinigameInfo.RaisePlayerValueChanged(slot, delta, newValue);

    [Rpc(SendTo.Everyone)]
    private void PlayerFinishedRpc(int slot) => MinigameInfo.RaisePlayerFinished(slot);


    // カウントダウンをして、プレイを始める(サーバーで呼ばれる)
    // async / await : ここで待つをゲームを止めずに書ける仕組み。awaitの行で1秒待つ間も、ゲームは動く
    // UniTask : 待つ処理を含むメソッドの戻り値の型。進行役が await で「終わるまで待つ」ためにUniTaskVoidではなくこちらにする
    // ct : 待っている途中でオブジェクトが消えたら(シーン移動など)、自動で待つのをやめるための合図
    private async UniTask ServerRunCountdownAsync(CancellationToken ct)
    {
        ServerSetPhase(MinigamePhase.Countdown);
        for(int i = countdownSeconds; i >= 1; i--)
        {
            // 今の時間を呼ぶ。
            CountdownRpc(i);
            // 1秒待つ
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
        }

        // スタートの合図
        CountdownRpc(0);
        // ラウンド制限時間を計算
        if(roundTimeLimit > 0)
        {
            roundEndServerTime.Value = NetworkManager.ServerTime.Time + roundTimeLimit;
        }
        else
        {
            roundEndServerTime.Value = -1;
        }
        // フェーズをプレイ中に(始まった時刻を覚えておく)
        roundStartServerTime.Value = NetworkManager.ServerTime.Time;
        ServerSetPhase(MinigamePhase.Playing);
    }


    // カウントダウンの数字を全員に知らせる(全員の端末で実行)。 0 = スタート！
    [Rpc(SendTo.Everyone)]
    private void CountdownRpc(int secondsLeft)
    {
        Debug.Log($"[MinigameBase]カウントダウン : {secondsLeft}");
        MinigameInfo.RaiseCountdown(secondsLeft);
    }


    /// <summary>
    /// 今のフェーズ
    /// =>とすることで読むだけのプロパティになる
    /// </summary>
    public MinigamePhase Phase => phase.Value;

    /// <summary>今のラウンド(1から数える。0 = まだ始まっていない)</summary>
    public int CurrentRound => currentRound.Value;

    /// <summary>全体のラウンド数</summary>
    public int TotalRounds => totalRounds;

    /// <summary> プレイヤーの人数 </summary>
    public int PlayerCount => players.Count;

    /// <summary> index 番目のプレイヤーの情報(0からPlayerCount - 1 まで)。枠番号の順(1P, 2P, …)に並んでいる。空き枠があると index と枠番号はずれる </summary>
    public MinigamePlayerState GetPlayer(int index) => players[index];

    /// <summary>値の種類(得点 / 時間 / 回数)</summary>
    public MinigameValueKind ValueKind => valueKind;

    /// <summary>このラウンドを終えた人の行動を受け付けない設定か(MinigameInfo.CanControl が使う)</summary>
    public bool IgnoreActionsAfterFinished => ignoreActionsAfterFinished;

    /// <summary>値を表示用の文字にする(設定の表示形式・単位を使う)。例: 12.34 → "12.34秒"</summary>
    public string FormatValue(float value) => value.ToString(valueFormat) + valueUnit;

    /// <summary>枠番号(0 = 1P)からプレイヤーの情報を探す。見つかれば true</summary>
    public bool TryGetPlayerBySlot(int slot, out MinigamePlayerState state)
    {
        int index = FindPlayerIndex(slot);
        state = index >= 0 ? players[index] : default;
        return index >= 0;
    }

    /// <summary>
    /// 残り時間(秒)。制限なし・プレイ中でないときは -1
    /// </summary>
    public float RemainingTime
    {
        get
        {
            if (!IsSpawned) { return -1; }
            // プレイ中でなければ -1(ラウンド終了の演出中に、残り時間が減り続けて見えないように)
            if (Phase != MinigamePhase.Playing || roundEndServerTime.Value < 0.0f) { return -1; }
            double remaining = roundEndServerTime.Value - NetworkManager.ServerTime.Time;
            return Mathf.Max(0f, (float)remaining);
        }
    }

    /// <summary>
    /// 今のラウンドのプレイが始まってからの秒数。プレイ中でなければ -1。
    /// 制限時間がないゲームでも使える。全員の画面でそろう(例: ストップウォッチの「0.00 → 1.23 → …」)
    /// </summary>
    public float ElapsedTime
    {
        get
        {
            if (!IsSpawned) { return -1; }
            if (Phase != MinigamePhase.Playing || roundStartServerTime.Value < 0) { return -1; }
            double elapsed = NetworkManager.ServerTime.Time - roundStartServerTime.Value;
            return Mathf.Max(0f, (float)elapsed);
        }
    }

    // フェーズを変える。サーバーだけ呼べる
    // 状態(NetworkVariable)を変えると同時に、「変わった」という出来事を RPC で全員に送る
    // (NetworkVariable だけだと、一瞬で過ぎたフェーズが参加者に届かないことがあるため)
    protected void ServerSetPhase(MinigamePhase next)
    {
        if (!IsServer)
        {
            Debug.LogWarning("ServerSetPhaseはサーバーでしか呼べません");
            return;
        }

        if (phase.Value == next) { return; } // 同じフェーズなら何もしない(イベントも鳴らさない)

        phase.Value = next;
        PhaseChangedRpc(next, currentRound.Value);
    }

    // フェーズが変わったことを全員に知らせる(全員の端末で実行)。確認用にログも出す
    [Rpc(SendTo.Everyone)]
    private void PhaseChangedRpc(MinigamePhase newPhase, int round)
    {
        Debug.Log($"[MinigameBase]フェーズ : {newPhase}(ラウンド {round})");
        MinigameInfo.RaisePhaseChanged(newPhase, round);
    }


    /// <summary>
    /// 今すぐラウンドを終える。サーバーでだけ呼べる(担当者が撮影した瞬間などに呼ぶ)
    /// </summary>
    protected void ServerEndRound()
    {
        // サーバーでないなら
        if(!IsServer)
        {
            Debug.LogWarning("ServerEndRoundはサーバーでしか呼べません");
            return;
        }

        // 時間切れと、担当者のServerEndRound()が同じフレームに重なっても、2回終わらないようにするため
        if (phase.Value != MinigamePhase.Playing) { return; }

        // ラウンド終了
        Debug.Log("ラウンド終了");
        ServerSetPhase(MinigamePhase.RoundEnd);
    }


    // ---------- ミニゲーム担当者が呼ぶメソッド(値の操作) ----------
    // NetworkList の中身は struct なので「取り出す → 書き換える → 戻す」で変える。戻した時点で全員に同期される

    /// <summary>今回のラウンドの値を設定する(サーバーのみ)。例: ストップウォッチで止めた時間の誤差</summary>
    protected void ServerSetValue(int slot, float value)
    {
        int index = ServerFindPlayerIndexChecked(slot, nameof(ServerSetValue));
        if (index < 0) { return; }

        MinigamePlayerState state = players[index];
        // まとめ方が「合計」なら、合計の値もその場で増やす(画面にリアルタイムで出せる)。
        // それ以外(ベスト・最後・勝ち点)は、ラウンドの終わりに ServerRecordRoundValues でまとめて計算する
        float delta = value - state.RoundValue;
        if (delta == 0f) { return; } // 変わっていなければ何もしない(イベントも鳴らさない)

        if (UsesLiveSum)
        {
            state.TotalValue += delta;
        }
        state.RoundValue = value;
        players[index] = state;

        PlayerValueChangedRpc(slot, delta, value);
    }

    /// <summary>今回のラウンドの値に足す(サーバーのみ)。例: パイが当たったら +1</summary>
    protected void ServerAddValue(int slot, float delta)
    {
        int index = ServerFindPlayerIndexChecked(slot, nameof(ServerAddValue));
        if (index < 0) { return; }

        ServerSetValue(slot, players[index].RoundValue + delta);
    }

    /// <summary>この人はこのラウンドを終えた、にする(サーバーのみ)。例: ストップウォッチを止めた</summary>
    protected void ServerSetFinished(int slot)
    {
        int index = ServerFindPlayerIndexChecked(slot, nameof(ServerSetFinished));
        if (index < 0) { return; }

        MinigamePlayerState state = players[index];
        if (state.IsFinished) { return; } // もう終えている(2回目はイベントを鳴らさない)

        state.IsFinished = true;
        players[index] = state;

        PlayerFinishedRpc(slot);
    }


    // ---------- ミニゲーム担当者が呼ぶメソッド(独自の情報・出来事) ----------

    /// <summary>独自の情報(状態)を全員に同期する(サーバーのみ)。同じ名前なら上書き。例: ServerSetInfo("目標時間", 10f)</summary>
    protected void ServerSetInfo(string infoName, MinigameValue value)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"[MinigameBase] {nameof(ServerSetInfo)} はサーバーでしか呼べません");
            return;
        }

        FixedString64Bytes key = new FixedString64Bytes(infoName);
        for (int i = 0; i < customInfos.Count; i++)
        {
            if (customInfos[i].Name != key) { continue; }

            // 同じ名前があれば上書きする(値が同じなら、送らずに済ませる)
            if (customInfos[i].Value.Equals(value)) { return; }
            customInfos[i] = new MinigameCustomInfo { Name = key, Value = value };
            return;
        }

        // 同じ名前がなければ追加する
        customInfos.Add(new MinigameCustomInfo { Name = key, Value = value });
    }

    /// <summary>
    /// 独自の出来事を全員に知らせる(サーバーのみ)。
    /// 例: ServerSendEvent("撮影") / ServerSendEvent("ストップ", 0.12f, slot)
    /// </summary>
    /// <param name="value">一緒に送る値(なくてもよい)</param>
    /// <param name="slot">誰の出来事か(枠番号)。誰のものでもなければ -1</param>
    protected void ServerSendEvent(string eventName, MinigameValue value = default, int slot = -1)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"[MinigameBase] {nameof(ServerSendEvent)} はサーバーでしか呼べません");
            return;
        }

        CustomEventRpc(new FixedString64Bytes(eventName), value, slot);
    }

    // 独自の出来事を全員に届ける(全員の端末で実行し、MinigameInfo のイベントを鳴らす)
    [Rpc(SendTo.Everyone)]
    private void CustomEventRpc(FixedString64Bytes eventName, MinigameValue value, int slot)
    {
        MinigameInfo.RaiseCustomEvent(eventName.ToString(), value, slot);
    }

    /// <summary>独自の情報を名前で探す。見つかれば true</summary>
    public bool TryGetInfo(string infoName, out MinigameValue value)
    {
        FixedString64Bytes key = new FixedString64Bytes(infoName);
        for (int i = 0; i < customInfos.Count; i++)
        {
            if (customInfos[i].Name == key)
            {
                value = customInfos[i].Value;
                return true;
            }
        }
        value = default;
        return false;
    }


    // ---------- 結果の後の行き先(ホストが選ぶ) ----------

    // 行き先をもう選んだか(サーバーだけが使う)。2回選ばれて遷移が重ならないようにする
    private bool nextChosen;

    /// <summary>
    /// 結果の後の行き先を選ぶ。ホストだけが選べて、Phase が Result のときだけ有効(参加者が呼んでも何も起きない)。
    /// Retry = 同じミニゲームをもう一度 / ToStageSelect = ミニゲーム選択へ / ToTitle = 部屋を解散してタイトルへ。
    /// UI からは MinigameInfo.ChooseNext で呼べる
    /// </summary>
    public void ChooseNext(MinigameNextAction action)
    {
        if (!IsServer)
        {
            Debug.Log("[MinigameBase] 次の行き先はホストだけが選べます");
            return;
        }
        if (Phase != MinigamePhase.Result)
        {
            Debug.LogWarning("[MinigameBase] 次の行き先は、結果(Result)のときだけ選べます");
            return;
        }
        if (nextChosen) { return; }

        NetworkSceneTransition transition = NetworkSceneTransition.Instance;
        if (transition == null)
        {
            Debug.LogWarning("[MinigameBase] NetworkSceneTransition が見つからないので、移動できません(タイトルから始めてください)");
            return;
        }

        nextChosen = true;
        NextChosenRpc(action); // UI に「ホストが〇〇を選んだ」を知らせる

        switch (action)
        {
            case MinigameNextAction.Retry:
                // 同じシーンを読み込み直す(全員が一緒に移動する)
                transition.LoadSceneForAllAsync(gameObject.scene.name).Forget();
                break;
            case MinigameNextAction.ToStageSelect:
                transition.LoadSceneForAllAsync(SceneNames.StageSelect).Forget();
                break;
            case MinigameNextAction.ToTitle:
                // 部屋を解散して、全員タイトルへ(参加者には「ホストが部屋を解散しました」と出る)
                transition.ReturnToTitleAsync().Forget();
                break;
        }
    }

    // 行き先が選ばれたことを全員に知らせる(全員の端末で実行)
    [Rpc(SendTo.Everyone)]
    private void NextChosenRpc(MinigameNextAction action)
    {
        Debug.Log($"[MinigameBase] 次の行き先: {action}");
        MinigameInfo.RaiseNextChosen(action);
    }


    // ---------- 行動の送信(端末 → サーバー) ----------

    /// <summary>
    /// 自分(この端末)の行動をサーバーに送る(どの端末からでも呼べる)。例: SendAction("Stop")。
    /// 押した瞬間のサーバー時刻は自動で付ける。プレイ中以外の行動と、脱落した人の行動はサーバーで無視される。
    /// UI や入力のスクリプトからは MinigameInfo.SendAction で呼べる
    /// </summary>
    public void SendAction(string actionName, MinigameValue value = default)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("[MinigameBase] ミニゲームが始まっていないので、行動を送れません");
            return;
        }

        PlayerActionRpc(new FixedString64Bytes(actionName), value, NetworkManager.ServerTime.Time);
    }

    // 行動を受け取る(サーバーで実行)。誰が送ったかは送信元のIDで決める(引数で「何P」を送らせないので、なりすましができない)
    [Rpc(SendTo.Server)]
    private void PlayerActionRpc(FixedString64Bytes actionName, MinigameValue value, double pressedServerTime, RpcParams rpcParams = default)
    {
        // プレイ中以外の操作は無視する
        if (Phase != MinigamePhase.Playing) { return; }

        // 送ってきた端末のプレイヤーを探す
        ulong senderId = rpcParams.Receive.SenderClientId;
        int index = -1;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].ClientId == senderId && !players[i].IsCpu)
            {
                index = i;
                break;
            }
        }
        if (index < 0) { return; }

        // 抜けた人・脱落した人の操作は無視する
        MinigamePlayerState player = players[index];
        if (!player.IsConnected || player.IsEliminated) { return; }

        // 設定がオンなら、このラウンドを終えた人の操作も無視する(2回押しても値が上書きされないように)
        if (ignoreActionsAfterFinished && player.IsFinished) { return; }

        // 押した時刻は端末から届く値なので、おかしな値にならないよう「プレイ開始 〜 今」の範囲に収める
        double now = NetworkManager.ServerTime.Time;
        pressedServerTime = Math.Clamp(pressedServerTime, roundStartServerTime.Value, now);

        OnServerPlayerAction(player.SlotIndex, actionName.ToString(), value, pressedServerTime);
    }

    /// <summary>今のラウンドのプレイが始まったサーバー時刻(サーバーのみ)。経過時間 = pressedServerTime - ServerRoundStartTime</summary>
    protected double ServerRoundStartTime => roundStartServerTime.Value;


    // ---------- ミニゲーム担当者が書く場所(すべて任意。上書きしたいものだけ override する) ----------
    // 名前が OnServer で始まるものは、サーバーでだけ呼ばれる

    /// <summary>全員そろった直後に1回だけ呼ばれる(サーバーのみ)</summary>
    protected virtual void OnServerGameSetup() { }

    /// <summary>プレイが始まったとき(カウントダウンの後)に呼ばれる(サーバーのみ)</summary>
    protected virtual void OnServerRoundStart(int round) { }

    /// <summary>プレイ中、毎フレーム呼ばれる(サーバーのみ)</summary>
    protected virtual void OnServerPlayUpdate(float deltaTime) { }

    /// <summary>ラウンドが終わった直後に呼ばれる。値を確定させる場所(サーバーのみ)。このあとラウンド終了演出(OnServerRoundEndAsync)になる</summary>
    protected virtual void OnServerRoundEnd(int round) { }

    /// <summary>次のラウンドの前に呼ばれる。ミニゲーム独自のリセットを書く(サーバーのみ)。キャラクターの位置は基盤が戻す</summary>
    protected virtual void OnServerRoundReset(int nextRound) { }

    /// <summary>
    /// 誰かが行動した(SendAction が届いた)ときに呼ばれる(サーバーのみ)。プレイ中・脱落していない人の行動だけが届く。
    /// pressedServerTime = 押した瞬間のサーバー時刻(通信の遅れを含まないので、タイミングを公平に比べられる)。
    /// CPU の行動は、サーバーからこれを直接呼んでもよい
    /// </summary>
    protected virtual void OnServerPlayerAction(int slot, string actionName, MinigameValue value, double pressedServerTime) { }

    // ---------- ミニゲーム担当者が書く場所(演出で待つ。すべて任意) ----------
    // 何も書かなければ、インスペクターの「演出」の秒数だけ待つ。
    // 秒数で決まらない演出(カメラが動き終わるまで など)は override して、終わるまで await する。
    // 設定の秒数も待ちたいときは、override の中で await base.On〇〇Async(...) を呼ぶ。
    // 待っている間はフェーズが Playing ではないので、プレイヤーは操作できない(CanControl = false)

    /// <summary>イントロ演出(最初の1回だけ)。これが終わるまで、最初のラウンドは始まらない(サーバーのみ)</summary>
    protected virtual UniTask OnServerIntroAsync(CancellationToken ct) => ServerWaitSecondsAsync(introSeconds, ct);

    /// <summary>ラウンド開始演出(毎ラウンド、カウントダウンの前)。例: 目標を見せる(サーバーのみ)</summary>
    protected virtual UniTask OnServerRoundIntroAsync(int round, CancellationToken ct) => ServerWaitSecondsAsync(roundIntroSeconds, ct);

    /// <summary>ラウンド終了演出(毎ラウンド、OnServerRoundEnd の後)。例: フィニッシュ！・結果を見せる(サーバーのみ)</summary>
    protected virtual UniTask OnServerRoundEndAsync(int round, CancellationToken ct) => ServerWaitSecondsAsync(roundEndSeconds, ct);

    /// <summary>ゲーム終了演出(最後のラウンドの後、順位を出す前)。例: 全員の結果を一斉に見せる(サーバーのみ)</summary>
    protected virtual UniTask OnServerGameEndAsync(CancellationToken ct) => ServerWaitSecondsAsync(gameEndSeconds, ct);

    /// <summary>指定した秒数だけ待つ(0 以下ならすぐ終わる)。演出の override の中で使える</summary>
    protected UniTask ServerWaitSecondsAsync(float seconds, CancellationToken ct)
    {
        if (seconds <= 0f) { return UniTask.CompletedTask; }
        return UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct);
    }

    // ネットワークに出てきたとき、全員の端末で呼ばれる。窓口に登録し、「準備できた」を送る
    public override void OnNetworkSpawn()
    {
        // 終了条件は、同じオブジェクトに Add Component で付けられたものを集めておく
        endConditions = GetComponents<MinigameEndCondition>();

        // UI 用の窓口(MinigameInfo)に、今動いているミニゲームとして登録する
        MinigameInfo.Register(this);

        NotifyReadyRpc();
    }
    // ネットワークから消えるとき、全員の端末で呼ばれる。進行役を止め、窓口から外す(イベントの登録も片付けられる)
    public override void OnNetworkDespawn()
    {
        // 通信が止まった後に、進行役がカウントダウンなどの合図を送ろうとしないように止める
        if (gameCts != null)
        {
            gameCts.Cancel();
            gameCts.Dispose();
            gameCts = null;
        }

        MinigameInfo.Unregister(this);
    }


    // 毎フレーム呼ばれる。サーバーで、プレイ中なら担当者の処理を呼び、時間切れか終了条件を満たしたらラウンドを終える
    private void Update()
    {
        if (!IsServer) { return; }
        if (phase.Value != MinigamePhase.Playing) { return; }

        OnServerPlayUpdate(Time.deltaTime);

        // 担当者が OnServerPlayUpdate の中で ServerEndRound() を呼んだ場合は、もう終わっている
        if (phase.Value != MinigamePhase.Playing) { return; }

        // 時刻を比べる(制限なしのときは見ない)
        if (roundEndServerTime.Value >= 0 && NetworkManager.ServerTime.Time >= roundEndServerTime.Value)
        {
            Debug.Log("[MinigameBase] 時間切れ");
            ServerEndRound();
            return;
        }

        // 付いている終了条件のうち、どれか1つでも満たしたら終わる
        foreach (MinigameEndCondition condition in endConditions)
        {
            if (condition.enabled && condition.IsMet(this))
            {
                Debug.Log($"[MinigameBase] 終了条件 {condition.GetType().Name} を満たしました");
                ServerEndRound();
                return;
            }
        }
    }
}