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
    [Tooltip("目標の秒数の最小値。毎ラウンド、最小～最大の間からランダムに決める。最小と最大を同じにすると固定になる")]
    [SerializeField] private int minTargetSeconds = 5;

    [Tooltip("目標の秒数の最大値")]
    [SerializeField] private int maxTargetSeconds = 15;

    // UI・入力の担当と名前をそろえる(1文字でも違うと届かない)
    private const string TargetInfoName = "目標時間";   // 情報 : 今のラウンドの目標の秒数
    private const string StopActionName = "Stop";       // 行動 : ストップボタンが押された
    private const string StopEventName = "ストップ";    // 出来事 : 誰かが止めた(値 = 止めた秒数)
    private const string TimeUpEventName = "時間切れ";  // 出来事 : 押さないまま時間切れになった(値 = 時間切れの秒数)

    // 今のラウンドの目標の秒数(サーバーだけが使う)
    private float targetSeconds;

    public override MinigameType Type => MinigameType.Stopwatch;


    // ラウンド開始演出
    // 目標の秒数を決めて全員に送り、その後設定の秒数(Round intro Seconds)だけ待つ
    protected override async UniTask OnServerRoundIntroAsync(int round, CancellationToken ct)
    {
        // int の Random.Range は最大の値を含まないので、+1して最大も選ばれるようにする
        targetSeconds = Random.Range(minTargetSeconds, maxTargetSeconds + 1);
        ServerSetInfo(TargetInfoName, targetSeconds);
        Debug.Log($"[Stopwatch]ラウンド{round}の目標：{targetSeconds}秒");

        // 設定の秒数だけ待つ
        await base.OnServerRoundIntroAsync(round, ct);
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
