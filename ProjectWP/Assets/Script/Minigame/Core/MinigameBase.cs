using System.Collections.Generic;
using System.Threading;
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

    [Header("ラウンド")]
    [Tooltip("ラウンド数。1 = ラウンド制ではない")]
    [SerializeField] private int totalRounds = 1;

    [Tooltip("1ラウンドの制限時間(秒)。0 = 制限なし")]
    [SerializeField] private float roundTimeLimit = 30.0f;

    [Header("リセット")]
    [Tooltip("次のラウンドの前に、キャラクターを初期位置に戻すときに使う。空欄ならシーンから自動で探す(見つからなければ位置は戻さない)")]
    [SerializeField] private PlayerSpawnPoints spawnPoints;

    [Tooltip("次のラウンドの前に位置を戻す間、全員の画面を暗くする。オフなら暗くせずにその場で戻す")]
    [SerializeField] private bool fadeOnRoundReset = true;

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

    // 「準備ができた」が届いた人のID(サーバーだけが使う)
    private readonly HashSet<ulong> readyClientIds = new HashSet<ulong>();



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
    // 演出(イントロ・ラウンド開始/終了・ゲーム終了)はステップ13、結果はステップ8で差し込む
    private async UniTaskVoid ServerRunGameAsync()
    {
        CancellationToken ct = destroyCancellationToken;

        OnServerGameSetup();

        // TODO(ステップ13): イントロ演出(Intro)

        for (int round = 1; round <= totalRounds; round++)
        {
            currentRound.Value = round;

            // TODO(ステップ13): ラウンド開始演出(RoundIntro)

            await ServerRunCountdownAsync(ct);
            OnServerRoundStart(round);

            // 時間切れ・終了条件・担当者の ServerEndRound() のどれかで Playing でなくなるまで待つ
            await UniTask.WaitUntil(() => Phase != MinigamePhase.Playing, cancellationToken: ct);
            OnServerRoundEnd(round);

            // TODO(ステップ13): ラウンド終了演出(RoundEnd)

            // 最後のラウンドでなければ、次のラウンドの準備をする
            if (round < totalRounds)
            {
                ServerSetPhase(MinigamePhase.RoundReset);
                await ServerResetForNextRoundAsync(round + 1, ct);
            }
        }

        ServerSetPhase(MinigamePhase.GameEnd);
        // TODO(ステップ13): ゲーム終了演出(GameEnd)
        // TODO(ステップ8): 順位を計算して全員に送る
        ServerSetPhase(MinigamePhase.Result);
    }

    // 次のラウンドの準備(サーバーで呼ばれる)。値とラウンド数は引き継ぎ、それ以外を戻す
    // 設定でフェードがオンなら、暗くしている間に位置を戻す
    private async UniTask ServerResetForNextRoundAsync(int nextRound, CancellationToken ct)
    {
        roundEndServerTime.Value = -1;

        // 暗くする(フェードアウトがオフなら一瞬で暗くする)
        if (fadeOnRoundReset)
        {
            float fadeOutSeconds = resetFadeOut ? resetFadeOutSeconds : 0f;
            FadeOutRpc(fadeOutSeconds);
            await UniTask.Delay(TimeSpan.FromSeconds(fadeOutSeconds), cancellationToken: ct);
        }

        if (spawnPoints == null)
        {
            spawnPoints = FindFirstObjectByType<PlayerSpawnPoints>();
        }
        if (spawnPoints != null)
        {
            spawnPoints.PlaceAllPlayers();
        }

        OnServerRoundReset(nextRound);

        // 明るくする(フェードインがオフなら一瞬で明るくする)。終わるまで待ってから次のラウンドへ
        if (fadeOnRoundReset)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(ResetMoveSettleSeconds), cancellationToken: ct);

            float fadeInSeconds = resetFadeIn ? resetFadeInSeconds : 0f;
            FadeInRpc(fadeInSeconds);
            await UniTask.Delay(TimeSpan.FromSeconds(fadeInSeconds), cancellationToken: ct);
        }
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
        // フェーズをプレイ中に
        ServerSetPhase(MinigamePhase.Playing);
    }


    // カウントダウンの数字を全員に知らせる(全員の端末で実行)。 0 = スタート！
    [Rpc(SendTo.Everyone)]
    private void CountdownRpc(int secondsLeft)
    {
        Debug.Log($"[MinigameBase]カウントダウン : {secondsLeft}");
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

    /// <summary>
    /// 残り時間(秒)。制限なしの時は-1
    /// </summary>
    public float RemainingTime
    {
        get
        {
            if (!IsSpawned) { return -1; }
            if (roundEndServerTime.Value < 0.0f) { return -1; }
            double remaining = roundEndServerTime.Value - NetworkManager.ServerTime.Time;
            return Mathf.Max(0f, (float)remaining);
        }
    }

    // フェーズを変える。サーバーだけ呼べる
    protected void ServerSetPhase(MinigamePhase next)
    {
        if (!IsServer)
        {
            Debug.LogWarning("ServerSetPhaseはサーバーでしか呼べません"); 
            return;
        }

        phase.Value = next;
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


    // ---------- ミニゲーム担当者が書く場所(すべて任意。上書きしたいものだけ override する) ----------
    // 名前が OnServer で始まるものは、サーバーでだけ呼ばれる

    /// <summary>全員そろった直後に1回だけ呼ばれる(サーバーのみ)</summary>
    protected virtual void OnServerGameSetup() { }

    /// <summary>プレイが始まったとき(カウントダウンの後)に呼ばれる(サーバーのみ)</summary>
    protected virtual void OnServerRoundStart(int round) { }

    /// <summary>プレイ中、毎フレーム呼ばれる(サーバーのみ)</summary>
    protected virtual void OnServerPlayUpdate(float deltaTime) { }

    /// <summary>ラウンドが終わったときに呼ばれる(サーバーのみ)</summary>
    protected virtual void OnServerRoundEnd(int round) { }

    /// <summary>次のラウンドの前に呼ばれる。ミニゲーム独自のリセットを書く(サーバーのみ)。キャラクターの位置は基盤が戻す</summary>
    protected virtual void OnServerRoundReset(int nextRound) { }

    // ネットワークに出てきたとき、全員の端末で呼ばれる。フェーズの変化を見張り始め、「準備できた」を送る
    public override void OnNetworkSpawn()
    {
        phase.OnValueChanged += OnPhaseChanged;
        NotifyReadyRpc();
    }
    // ネットワークから消えるとき、全員の端末で呼ばれる。見張りをやめる。
    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;
    }
    // フェーズが変わったとき、全員の端末で呼ばれる。確認用にログを出す
    private void OnPhaseChanged(MinigamePhase previous, MinigamePhase current)
    {
        Debug.Log($"[MinigameBase]フェーズ : {previous} → {current}");
    }


    // 毎フレーム呼ばれる。サーバーで、プレイ中なら担当者の処理を呼び、時間切れならラウンドを終える
    private void Update()
    {
        if (!IsServer) { return; }
        if (phase.Value != MinigamePhase.Playing) { return; }

        OnServerPlayUpdate(Time.deltaTime);

        // 時刻を比べる(制限なしのときは見ない)
        if (roundEndServerTime.Value >= 0 && NetworkManager.ServerTime.Time >= roundEndServerTime.Value)
        {
            ServerEndRound();
        }
    }
}