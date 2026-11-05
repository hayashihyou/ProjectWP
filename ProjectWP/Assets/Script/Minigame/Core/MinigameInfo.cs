using System;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// UI・演出担当のための、ミニゲームの情報の窓口(各端末で使う)。
/// Netcode を知らなくても、ここのプロパティを読む・イベントを購読するだけで必要な情報が取れる。
/// 入力のスクリプトは SendAction で、自分の行動(ボタンを押した など)をサーバーに送れる。
///
/// ・今の値(プロパティ)… いつ読んでも最新の値が返る。毎フレーム読んでよい
/// ・出来事(イベント)  … 「今起きた」という合図。演出やエフェクトのきっかけに使う。後から購読しても過去の分は来ない
///
/// どのイベントも、ホストを含む各端末で1回ずつ呼ばれる。
/// イベントは OnEnable で += 、OnDisable で -= するのがおすすめ(ミニゲームが終わると登録は自動で全部消える)。
///
/// 注意: 出来事(イベント)は、今の値(プロパティ)より先に届くことがある(通信の仕組み上の順番)。
/// 例えば OnCountdown が来た瞬間に Phase がまだ Countdown になっていないことがある。
/// イベントの引数には必要な値を入れてあるので、イベントの中では引数を使うこと。
/// </summary>
public static class MinigameInfo
{
    // 今動いているミニゲーム(MinigameBase がスポーンしたときに登録される)
    private static MinigameBase current;

    // 最後の結果。シーンをまたいでも残る(結果画面を別のシーンで共通化する場合に備えて)
    private static MinigameRankEntry[] lastResult;


    // ==================== 今の値 ====================

    /// <summary>ミニゲームが動いているか(false のときは、下の値はすべて初期値を返す)</summary>
    public static bool IsActive => current != null && current.IsSpawned;

    /// <summary>今動いているミニゲームの種類(動いてなければNone)</summary>
    public static MinigameType CurrentType => IsActive ? current.Type : MinigameType.None;

    /// <summary>今のフェーズ</summary>
    public static MinigamePhase Phase => IsActive ? current.Phase : MinigamePhase.WaitingForPlayers;

    /// <summary>今のラウンド(1から数える。0 = まだ始まっていない)</summary>
    public static int CurrentRound => IsActive ? current.CurrentRound : 0;

    /// <summary>全体のラウンド数</summary>
    public static int TotalRounds => IsActive ? current.TotalRounds : 0;

    /// <summary>残り時間(秒)。制限なし・プレイ中でないときは -1</summary>
    public static float RemainingTime => IsActive ? current.RemainingTime : -1f;

    /// <summary>今のラウンドのプレイが始まってからの秒数。プレイ中でなければ -1。例: ストップウォッチの「0.00 → 1.23 → …」</summary>
    public static float ElapsedTime => IsActive ? current.ElapsedTime : -1f;

    /// <summary>最後に終わったラウンドが、制限時間で終わったか(OnRoundEnded の中で読める)。例: 「タイムアップ!」を出すか「フィニッシュ!」を出すか</summary>
    public static bool LastRoundTimedUp { get; private set; }

    /// <summary>プレイヤーの人数</summary>
    public static int PlayerCount => IsActive ? current.PlayerCount : 0;

    /// <summary>index 番目のプレイヤーの情報(0 から PlayerCount - 1 まで。枠番号の順)</summary>
    public static MinigamePlayerState GetPlayer(int index) => current.GetPlayer(index);

    /// <summary>
    /// 全員の情報(枠番号の順)。読むたびに新しいリストを作るので、毎フレーム読むなら PlayerCount と GetPlayer の方が軽い
    /// </summary>
    public static IReadOnlyList<MinigamePlayerState> Players
    {
        get
        {
            List<MinigamePlayerState> list = new List<MinigamePlayerState>();
            for (int i = 0; i < PlayerCount; i++) { list.Add(GetPlayer(i)); }
            return list;
        }
    }

    /// <summary>枠番号(0 = 1P)からプレイヤーの情報を探す。見つかれば true</summary>
    public static bool TryGetPlayerBySlot(int slot, out MinigamePlayerState player)
    {
        player = default;
        return IsActive && current.TryGetPlayerBySlot(slot, out player);
    }

    /// <summary>自分(この端末)のプレイヤーがいるか</summary>
    public static bool HasLocalPlayer => TryGetLocalPlayer(out _);

    /// <summary>自分(この端末)のプレイヤーの情報。いなければ初期値(HasLocalPlayer で確かめられる)</summary>
    public static MinigamePlayerState LocalPlayer
    {
        get
        {
            TryGetLocalPlayer(out MinigamePlayerState player);
            return player;
        }
    }

    /// <summary>自分(この端末)のプレイヤーの情報を探す。見つかれば true</summary>
    public static bool TryGetLocalPlayer(out MinigamePlayerState player)
    {
        player = default;
        if (!IsActive || NetworkManager.Singleton == null) { return false; }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < PlayerCount; i++)
        {
            MinigamePlayerState p = GetPlayer(i);
            if (p.ClientId == localId && !p.IsCpu)
            {
                player = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 今操作してよいか(プレイ中で、自分が脱落していない)。
    /// 「終えた人の行動は受け付けない」設定がオンなら、このラウンドを終えた後も false になる
    /// </summary>
    public static bool CanControl
    {
        get
        {
            if (Phase != MinigamePhase.Playing) { return false; }
            if (!TryGetLocalPlayer(out MinigamePlayerState player)) { return false; }
            if (player.IsEliminated || !player.IsConnected) { return false; }
            if (current.IgnoreActionsAfterFinished && player.IsFinished) { return false; }
            return true;
        }
    }

    /// <summary>値の種類(得点 / 時間 / 回数)。表示の切り替えに使える</summary>
    public static MinigameValueKind ValueKind => IsActive ? current.ValueKind : MinigameValueKind.Score;

    /// <summary>値を表示用の文字にする(設定の表示形式・単位を使う)。例: 12.34 → "12.34秒"</summary>
    public static string FormatValue(float value) => IsActive ? current.FormatValue(value) : value.ToString();

    /// <summary>最後の結果(順位の良い順)。まだ結果が出ていなければ null。シーンをまたいでも残る</summary>
    public static IReadOnlyList<MinigameRankEntry> LastResult => lastResult;

    /// <summary>独自の情報を名前で読む。例: MinigameInfo.GetInfo("目標時間").FloatValue。なければ中身なし(Type = None)</summary>
    public static MinigameValue GetInfo(string infoName)
    {
        TryGetInfo(infoName, out MinigameValue value);
        return value;
    }

    /// <summary>独自の情報を名前で探す。見つかれば true</summary>
    public static bool TryGetInfo(string infoName, out MinigameValue value)
    {
        value = default;
        return IsActive && current.TryGetInfo(infoName, out value);
    }


    // ==================== 準備確認(全員が「準備OK」を押すまで待つ) ====================

    /// <summary>今「準備OK」を受け付けているか(Phase が ReadyCheck)</summary>
    public static bool IsReadyCheck => Phase == MinigamePhase.ReadyCheck;

    /// <summary>今のサーバー時刻(秒)。全員の端末でそろっている。ミニゲームが動いていなければ 0</summary>
    public static double ServerTime =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : 0;

    /// <summary>ReadyUI を出す時刻(サーバー時刻)。全員がこの時刻に出すので、表示がそろう。まだ決まっていなければ -1</summary>
    public static double ReadyUiShowServerTime { get; private set; } = -1;

    /// <summary>ReadyUI を消し始める時刻(サーバー時刻)。全員そろったときに決まる。まだなら -1</summary>
    public static double ReadyUiHideServerTime { get; private set; } = -1;

    /// <summary>
    /// 今「準備OK」を押してよいか(準備確認中で、ReadyUI が出る時刻を過ぎている)。
    /// ReadyUI が出る前に押されないように、入力のスクリプトはこれを見る
    /// </summary>
    public static bool CanSendReady => IsReadyCheck && (ReadyUiShowServerTime < 0 || ServerTime >= ReadyUiShowServerTime);

    /// <summary>その枠(0 = 1P)の人が「準備OK」を押したか</summary>
    public static bool IsPlayerReady(int slot)
    {
        MinigamePlayerState player;
        return TryGetPlayerBySlot(slot, out player) && player.IsReady;
    }

    /// <summary>自分(この端末)が「準備OK」を押したか</summary>
    public static bool IsLocalPlayerReady
    {
        get
        {
            MinigamePlayerState player;
            return TryGetLocalPlayer(out player) && player.IsReady;
        }
    }

    /// <summary>
    /// 自分(この端末)の「準備OK」をサーバーに送る。例: 決定ボタンが押されたら MinigameInfo.SendReady()。
    /// Phase が ReadyCheck のときだけ有効(それ以外はサーバーで無視される)。2回送っても1回分
    /// </summary>
    public static void SendReady()
    {
        if (!IsActive)
        {
            UnityEngine.Debug.LogWarning("[MinigameInfo] ミニゲームが動いていないので、準備OKを送れません");
            return;
        }
        current.SendReady();
    }


    // ==================== 結果の後の行き先(ホストが選ぶ) ====================

    /// <summary>この端末がホストか(結果の後の行き先を選べるのはホストだけ)</summary>
    public static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>
    /// 結果の後の行き先を選ぶ。例: 結果画面のボタンから MinigameInfo.ChooseNext(MinigameNextAction.Retry)。
    /// ホストだけが選べて、Phase が Result のときだけ有効(参加者が押しても何も起きない)。全員が一緒に移動する
    /// </summary>
    public static void ChooseNext(MinigameNextAction action)
    {
        if (!IsActive)
        {
            UnityEngine.Debug.LogWarning("[MinigameInfo] ミニゲームが動いていないので、行き先を選べません");
            return;
        }
        current.ChooseNext(action);
    }


    // ==================== 行動を送る(この端末 → サーバー) ====================

    /// <summary>
    /// 自分(この端末)の行動をサーバーに送る。例: ボタンが押されたら MinigameInfo.SendAction("Stop")。
    /// 押した瞬間の時刻は自動で付く。プレイ中以外・脱落した人の行動はサーバーで無視される(CanControl で事前に確かめられる)
    /// </summary>
    public static void SendAction(string actionName, MinigameValue value = default)
    {
        if (!IsActive)
        {
            UnityEngine.Debug.LogWarning("[MinigameInfo] ミニゲームが動いていないので、行動を送れません");
            return;
        }
        current.SendAction(actionName, value);
    }


    // ==================== 出来事(イベント) ====================

    /// <summary>フェーズが変わった(新しいフェーズ, 今のラウンド)。どのフェーズも、一瞬で過ぎた場合でも必ず1回ずつ届く。演出の開始のきっかけに使える</summary>
    public static event Action<MinigamePhase, int> OnPhaseChanged;

    /// <summary>カウントダウン(残り秒。3, 2, 1, 0 = スタート！)</summary>
    public static event Action<int> OnCountdown;

    /// <summary>ラウンドのプレイが始まった(ラウンド番号)</summary>
    public static event Action<int> OnRoundStarted;

    /// <summary>ラウンドが終わった(ラウンド番号)</summary>
    public static event Action<int> OnRoundEnded;

    /// <summary>誰かの今回の値が変わった(枠番号, 変化量, 新しい値)。例: 「+3点」のアニメーション</summary>
    public static event Action<int, float, float> OnPlayerValueChanged;

    /// <summary>誰かがこのラウンドを終えた(枠番号)。例: 「ストップ！」の表示</summary>
    public static event Action<int> OnPlayerFinished;

    /// <summary>誰かが脱落した(枠番号)。TODO(ステップ12)</summary>
    public static event Action<int> OnPlayerEliminated;

    /// <summary>誰かが途中で抜けた(枠番号)。TODO(ステップ12)</summary>
    public static event Action<int> OnPlayerLeft;

    /// <summary>ゲームが終わって結果が届いた(結果。順位の良い順)</summary>
    public static event Action<IReadOnlyList<MinigameRankEntry>> OnGameFinished;

    /// <summary>独自の出来事(名前, 値, 誰の出来事か = 枠番号。誰のものでもなければ -1)。例: 「撮影」でフラッシュを出す</summary>
    public static event Action<string, MinigameValue, int> OnCustomEvent;

    /// <summary>ホストが結果の後の行き先を選んだ(選ばれた行き先)。例: 参加者の画面に「もう一度遊びます」と出す</summary>
    public static event Action<MinigameNextAction> OnNextChosen;

    /// <summary>誰かが「準備OK」を押した(枠番号)。例: その人のパネルを「OK!」にする</summary>
    public static event Action<int> OnPlayerReady;

    /// <summary>全員の「準備OK」がそろった。例: ReadyUI を消す</summary>
    public static event Action OnAllPlayersReady;


    // ==================== ここから下は MinigameBase だけが使う ====================

    internal static void Register(MinigameBase game)
    {
        current = game;
    }

    internal static void Unregister(MinigameBase game)
    {
        if (current != game) { return; }
        current = null;

        // ミニゲームが終わったら、イベントの登録が残らないように片付ける
        OnPhaseChanged = null;
        OnCountdown = null;
        OnRoundStarted = null;
        OnRoundEnded = null;
        OnPlayerValueChanged = null;
        OnPlayerFinished = null;
        OnPlayerEliminated = null;
        OnPlayerLeft = null;
        OnGameFinished = null;
        OnCustomEvent = null;
        OnNextChosen = null;
        OnPlayerReady = null;
        OnAllPlayersReady = null;
        ReadyUiShowServerTime = -1;
        ReadyUiHideServerTime = -1;
    }

    internal static void RaisePhaseChanged(MinigamePhase phase, int round) => OnPhaseChanged?.Invoke(phase, round);
    internal static void RaiseCountdown(int secondsLeft) => OnCountdown?.Invoke(secondsLeft);
    internal static void RaiseRoundStarted(int round) => OnRoundStarted?.Invoke(round);
    internal static void RaiseRoundEnded(int round, bool timedUp)
    {
        LastRoundTimedUp = timedUp;
        OnRoundEnded?.Invoke(round);
    }
    internal static void RaisePlayerValueChanged(int slot, float delta, float newValue) => OnPlayerValueChanged?.Invoke(slot, delta, newValue);
    internal static void RaisePlayerFinished(int slot) => OnPlayerFinished?.Invoke(slot);
    internal static void RaisePlayerEliminated(int slot) => OnPlayerEliminated?.Invoke(slot);
    internal static void RaisePlayerLeft(int slot) => OnPlayerLeft?.Invoke(slot);
    internal static void RaiseCustomEvent(string eventName, MinigameValue value, int slot) => OnCustomEvent?.Invoke(eventName, value, slot);
    internal static void RaiseNextChosen(MinigameNextAction action) => OnNextChosen?.Invoke(action);
    internal static void RaisePlayerReady(int slot) => OnPlayerReady?.Invoke(slot);
    internal static void SetReadyUiShowTime(double showServerTime)
    {
        ReadyUiShowServerTime = showServerTime;
        ReadyUiHideServerTime = -1;
    }
    internal static void RaiseAllPlayersReady(double hideServerTime)
    {
        ReadyUiHideServerTime = hideServerTime;
        OnAllPlayersReady?.Invoke();
    }

    internal static void SetResultAndRaise(MinigameRankEntry[] result)
    {
        lastResult = result;
        OnGameFinished?.Invoke(result);
    }
}
