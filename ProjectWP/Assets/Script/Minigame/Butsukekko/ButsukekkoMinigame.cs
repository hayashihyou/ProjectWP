using UnityEngine;

/// <summary>
/// みんなでぶつけっこ : ステージのボールを拾って相手に投げる。
/// 当てた人に1点。30秒で一番点が高い人の勝ち
/// </summary>
public class ButsukekkoMinigame : MinigameBase
{
    [Header("得点")]
    [Tooltip("当てた人に入る点数")]
    [SerializeField] private int hitScore = 1;

    [Tooltip("当てられた人から引く点数。0 = 引かない")]
    [SerializeField] private int hitPenalty = 0;

    // 入力・UI・演出の担当と名前をそろえる(1文字でも違うと届かない)
    // 他のスクリプトからは ButsukekkoMinigame.ThrowEventName のように使うと打ち間違いがない
    public const string ThrowActionName = "Throw"; // 行動 : 投げるボタンが押された
    public const string ThrowEventName = "投げる";  // 出来事 : 誰かが投げ始めた(slot = 投げた人)
    public const string HitEventName = "ヒット";    // 出来事 : 誰かに当たった(値 = 当てた人の枠番号, slot = 当てられた人の枠番号)

    private ButsukekkoBall[] balls = new ButsukekkoBall[0];

    // このミニゲームの種類(MinigameType に追加済みの Butsukekko を返す)
    public override MinigameType Type => MinigameType.Butsukekko;


    // ゲーム全体で1回だけ : シーンにあるボールを集めておく
    protected override void OnServerGameSetup()
    {
        FindBalls();
    }

    // プレイの始め : ボールを最初の位置に戻す
    protected override void OnServerRoundStart(int round)
    {
        foreach (ButsukekkoBall ball in balls)
        {
            if (ball != null) { ball.ServerResetBall(); }
        }
    }

    // ボタンが押されたとき : その人がボールを持っていれば投げる
    protected override void OnServerPlayerAction(int slot, string actionName, MinigameValue value, double pressedServerTime)
    {
        if (actionName != ThrowActionName) { return; }
        if (balls.Length == 0) { FindBalls(); }

        foreach (ButsukekkoBall ball in balls)
        {
            if (ball == null || ball.HolderSlot != slot) { continue; }

            if (ball.ServerTryThrow())
            {
                // 全員の画面で、投げた人の投げるアニメーションを再生する
                ServerSendEvent(ThrowEventName, 0, slot);
            }
            return;
        }
    }

    /// <summary>
    /// ボールが当たったことを知らせる(サーバーでだけ有効)。ButsukekkoBall から呼ばれる。
    /// プレイ中以外、自分に当たった場合、枠番号が不正な場合は何もしない
    /// </summary>
    public void ServerReportHit(int throwerSlot, int hitSlot)
    {
        if (!IsServer) { return; }
        if (Phase != MinigamePhase.Playing) { return; }
        if (throwerSlot < 0 || hitSlot < 0 || throwerSlot == hitSlot) { return; }

        if (hitScore != 0) { ServerAddValue(throwerSlot, hitScore); }
        if (hitPenalty != 0) { ServerAddValue(hitSlot, -hitPenalty); }

        // 演出・音の担当に「〇Pが△Pに当てた」を知らせる
        ServerSendEvent(HitEventName, throwerSlot, hitSlot);

        Debug.Log($"[Butsukekko] {throwerSlot + 1}P が {hitSlot + 1}P に当てた");
    }

    private void FindBalls()
    {
        balls = FindObjectsByType<ButsukekkoBall>(FindObjectsSortMode.None);
    }
}