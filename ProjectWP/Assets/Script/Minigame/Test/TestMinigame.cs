using UnityEngine;

/// <summary>
/// 基盤の動作確認用ミニゲーム(ルール側)。画面の表示は TestMinigameInfoDisplay が MinigameInfo だけを使って行う
/// </summary>
public class TestMinigame : MinigameBase
{
    [Header("確認用")]
    [Tooltip("今回の値がこれ以上になった人を「このラウンドを終えた」にする。0 = しない(終了条件「全員が終えた」の確認用)")]
    [SerializeField] private float finishAtValue = 0f;

    // 確認用: 値を足す間隔(秒)
    private const float AddInterval = 1f;

    // 確認用: 次に値を足すまでの時間(サーバーだけが使う)
    private float addTimer;

    // 確認用: プレイ中、1秒ごとに「枠番号 + 1」点ずつ足す(1Pは+1, 2Pは+2 …)。人ごとに違う値になるので同期を確かめやすい
    protected override void OnServerPlayUpdate(float deltaTime)
    {
        addTimer += deltaTime;
        if (addTimer < AddInterval) { return; }
        addTimer -= AddInterval;

        for (int i = 0; i < PlayerCount; i++)
        {
            MinigamePlayerState player = GetPlayer(i);
            if (player.IsFinished) { continue; } // 終えた人の値は止める

            int slot = player.SlotIndex;
            ServerAddValue(slot, slot + 1);

            if (finishAtValue > 0f && GetPlayer(i).RoundValue >= finishAtValue)
            {
                ServerSetFinished(slot);
                ServerSendEvent("ストップ", GetPlayer(i).RoundValue, slot); // 出来事: 値と「誰の」付き
            }
        }
    }

    // 確認用: ラウンドの始めにタイマーを戻し、独自の情報と出来事を送る
    protected override void OnServerRoundStart(int round)
    {
        addTimer = 0f;

        ServerSetInfo("目標", round * 10);          // 情報: ラウンド1なら10、2なら20…
        ServerSendEvent("ラウンドの合図", round);   // 出来事: 値付き
    }
}
