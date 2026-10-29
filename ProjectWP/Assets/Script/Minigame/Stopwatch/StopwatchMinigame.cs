using System.Threading;
using System.Xml.Schema;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.PlayerLoop;

/// <summary>
/// 連続ストップウォッチ : 目標の秒数ピッタリでストップする。3ラウンドの誤差の合計が小さいほど勝ち
/// </summary>
public class StopwatchMinigame : MinigameBase
{
    [Header("ストップウォッチ")]
    [Tooltip("目標の秒数の最小値。毎ラウンド、最小～最大の間から刻みごとにランダムに決める。最小と最大を同じにすると固定になる")]
    [SerializeField] private float minTargetSeconds = 7.5f;

    [Tooltip("目標の秒数の最大値")]
    [SerializeField] private float maxTargetSeconds = 15f;

    [Tooltip("目標の秒数の刻み。0.5 なら 7.5, 8.0, 8.5 … のどれかになる")]
    [SerializeField] private float targetStepSeconds = 0.5f;

    [Header("演出の時間")]
    [Tooltip("カメラが定点に戻ってから、計測を始めるまでの間(秒)。「スタート！」の余韻。" +
             "このゲームでは Intro Seconds・Round Intro Seconds は使わず、カメラの演出の長さから待つ時間を決める")]
    [SerializeField] private float startDelaySeconds = 2.0f;

    // UI・入力の担当と名前をそろえる(1文字でも違うと届かない)
    // public にしてあるので、他のスクリプトからは StopwatchMinigame.StopEventName のように使うと打ち間違いがない
    public const string TargetInfoName = "目標時間";   // 情報 : 今のラウンドの目標の秒数
    public const string StopActionName = "Stop";       // 行動 : ストップボタンが押された
    public const string StopEventName = "ストップ";    // 出来事 : 誰かが止めた(値 = 止めた秒数)
    public const string TimeUpEventName = "時間切れ";  // 出来事 : 押さないまま時間切れになった(値 = 時間切れの秒数)
    public const string TargetEventName = "目標決定";  // 出来事 : 新しいラウンドの目標が決まった(値 = 目標の秒数)

    // 今のラウンドの目標の秒数(サーバーだけが使う)
    private float targetSeconds;

    public override MinigameType Type => MinigameType.Stopwatch;


    // イントロ演出(最初の1回だけ)
    // 1ラウンド目の目標はここで決め、イントロのカメラがテレビに着いたタイミングでテレビに出す
    protected override async UniTask OnServerIntroAsync(CancellationToken ct)
    {
        PickTarget(1);

        MinigameIntroCamera introCamera = FindFirstObjectByType<MinigameIntroCamera>();
        if (introCamera == null)
        {
            // カメラの演出がなければ、すぐに出して設定の秒数(Intro Seconds)だけ待つ
            SendTarget();
            await base.OnServerIntroAsync(ct);
            return;
        }

        // カメラがテレビに着くまで待ってから出す
        await ServerWaitSecondsAsync(introCamera.MonitorArriveSeconds, ct);
        SendTarget();

        // カメラが定点に戻りきるまで待つ(全員の画面で少し遅れて動くので、0.5秒の余裕を持たせる)
        await ServerWaitSecondsAsync(introCamera.TotalSeconds - introCamera.MonitorArriveSeconds + 0.5f, ct);
    }

    // ラウンド開始演出
    // 2ラウンド目からは、暗い間にカメラがテレビに寄っているので、明るくなってから目標を出し、カメラが戻るのを待つ
    // 最後に「スタート！」の余韻(startDelaySeconds)だけ待ってから、計測を始める
    protected override async UniTask OnServerRoundIntroAsync(int round, CancellationToken ct)
    {
        // 1ラウンド目の目標は、イントロでもう出している
        if (round > 1)
        {
            PickTarget(round);

            StopwatchRoundCamera roundCamera = FindFirstObjectByType<StopwatchRoundCamera>();
            if (roundCamera != null)
            {
                // テレビに寄った画面を少し見せてから目標を出し、カメラが定点に戻るまで待つ
                await ServerWaitSecondsAsync(roundCamera.TargetDelaySeconds, ct);
                SendTarget();
                await ServerWaitSecondsAsync(roundCamera.SecondsAfterTarget, ct);
            }
            else
            {
                SendTarget();
            }
        }

        Debug.Log($"[Stopwatch]ラウンド{round}: カメラが戻った。{startDelaySeconds}秒後に計測開始");
        await ServerWaitSecondsAsync(startDelaySeconds, ct);
    }

    // 目標の秒数を決めて、情報として全員に同期する(テレビに出すのは SendTarget)
    private void PickTarget(int round)
    {
        // 最小から最大までに、刻みが何個入るか(例: 7.5～15 を 0.5 刻み → 15個。7.5 自身を入れて16通り)
        int steps = targetStepSeconds > 0f ? Mathf.FloorToInt((maxTargetSeconds - minTargetSeconds) / targetStepSeconds + 0.001f) : 0;

        // int の Random.Range は最大の値を含まないので、+1して最大も選ばれるようにする
        targetSeconds = minTargetSeconds + Random.Range(0, Mathf.Max(0, steps) + 1) * targetStepSeconds;
        ServerSetInfo(TargetInfoName, targetSeconds);
        Debug.Log($"[Stopwatch]ラウンド{round}の目標：{targetSeconds}秒");
    }

    // 決めた目標を「出来事」で全員に知らせる。テレビはこれを受け取った瞬間に表示する
    // (情報の同期はフェーズの合図より遅れて届くことがあるので、表示のきっかけには出来事を使う)
    private void SendTarget()
    {
        ServerSendEvent(TargetEventName, targetSeconds);
    }


    // ボタンが押されたとき(プレイ中だけ届く。一度止めた人は2回目は基盤が止めるので届かない)
    protected override void OnServerPlayerAction(int slot, string actionName, MinigameValue value, double pressedServerTime)
    {
        if (actionName != StopActionName) { return; }

        // 何秒で止めたか(押した瞬間の時刻 - プレイが始まった時刻)
        float elapsed = (float)(pressedServerTime - ServerRoundStartTime);

        // 目標とのずれ(早くても遅くても、ずれた分だけ)
        float error = Mathf.Abs(elapsed - targetSeconds);

        ServerSetValue(slot, error);                    // この人の今回の値
        ServerSetFinished(slot);                        // この人は止めた
        ServerSendEvent(StopEventName, elapsed, slot);  // UIに「〇Pが△秒で止めた」を知らせる

        Debug.Log($"[Stopwatch] {slot + 1}P が {elapsed:0.00}秒 で止めた(誤差 {error:0.00}秒)");
    }

    // ラウンドが終わった直後(値を確定させる場所)
    // 時間切れまで押さなかった人は「時間切れの瞬間に止めた」扱いにして、誤差を入れる
    protected override void OnServerRoundEnd(int round)
    {
        float elapsed = (float)(NetworkManager.ServerTime.Time - ServerRoundStartTime);

        for (int i = 0; i < PlayerCount; i++)
        {
            MinigamePlayerState player = GetPlayer(i);
            if (player.IsFinished) { continue; }

            float error = Mathf.Abs(elapsed - targetSeconds);
            ServerSetValue(player.SlotIndex, error);
            ServerSendEvent(TimeUpEventName, elapsed, player.SlotIndex); // UIに「〇P は時間切れ」を知らせる

            Debug.Log($"[Stopwatch]{player.SlotIndex + 1}P は時間切れ(誤差{error:0.00}秒)");
        }
    }
}
