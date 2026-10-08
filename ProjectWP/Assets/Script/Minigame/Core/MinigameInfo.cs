using System;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// UI・演出担当のための、ミニゲームの情報の窓口(各端末で使う)。
/// Netcode を知らなくても、ここのプロパティを読む・イベントを購読するだけで必要な情報が取れる。
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

    /// <summary>今のフェーズ</summary>
    public static MinigamePhase Phase => IsActive ? current.Phase : MinigamePhase.WaitingForPlayers;

    /// <summary>今のラウンド(1から数える。0 = まだ始まっていない)</summary>
    public static int CurrentRound => IsActive ? current.CurrentRound : 0;

    /// <summary>全体のラウンド数</summary>
    public static int TotalRounds => IsActive ? current.TotalRounds : 0;

    /// <summary>残り時間(秒)。制限なし・プレイ中でないときは -1</summary>
    public static float RemainingTime => IsActive ? current.RemainingTime : -1f;

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

    /// <summary>今操作してよいか(プレイ中で、自分が脱落していない)</summary>
    public static bool CanControl
    {
        get
        {
            if (Phase != MinigamePhase.Playing) { return false; }
            return TryGetLocalPlayer(out MinigamePlayerState player) && !player.IsEliminated && player.IsConnected;
        }
    }

    /// <summary>値の種類(得点 / 時間 / 回数)。表示の切り替えに使える</summary>
    public static MinigameValueKind ValueKind => IsActive ? current.ValueKind : MinigameValueKind.Score;

    /// <summary>値を表示用の文字にする(設定の表示形式・単位を使う)。例: 12.34 → "12.34秒"</summary>
    public static string FormatValue(float value) => IsActive ? current.FormatValue(value) : value.ToString();

    /// <summary>最後の結果(順位の良い順)。まだ結果が出ていなければ null。シーンをまたいでも残る</summary>
    public static IReadOnlyList<MinigameRankEntry> LastResult => lastResult;


    // ==================== 出来事(イベント) ====================

    /// <summary>フェーズが変わった(新しいフェーズ)。間のフェーズが一瞬で過ぎると飛ばされることがある</summary>
    public static event Action<MinigamePhase> OnPhaseChanged;

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
    }

    internal static void RaisePhaseChanged(MinigamePhase phase) => OnPhaseChanged?.Invoke(phase);
    internal static void RaiseCountdown(int secondsLeft) => OnCountdown?.Invoke(secondsLeft);
    internal static void RaiseRoundStarted(int round) => OnRoundStarted?.Invoke(round);
    internal static void RaiseRoundEnded(int round) => OnRoundEnded?.Invoke(round);
    internal static void RaisePlayerValueChanged(int slot, float delta, float newValue) => OnPlayerValueChanged?.Invoke(slot, delta, newValue);
    internal static void RaisePlayerFinished(int slot) => OnPlayerFinished?.Invoke(slot);
    internal static void RaisePlayerEliminated(int slot) => OnPlayerEliminated?.Invoke(slot);
    internal static void RaisePlayerLeft(int slot) => OnPlayerLeft?.Invoke(slot);

    internal static void SetResultAndRaise(MinigameRankEntry[] result)
    {
        lastResult = result;
        OnGameFinished?.Invoke(result);
    }
}
