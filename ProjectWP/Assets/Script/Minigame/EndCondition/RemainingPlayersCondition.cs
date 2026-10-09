using UnityEngine;

/// <summary>
/// 終了条件: 残っている人(脱落していない、かつ接続中)が、決めた人数以下になったら終わる。
/// 例: 最後の1人になったら終わる(1)/ 全員脱落したら終わる(0)。
/// </summary>
[AddComponentMenu("Minigame/終了条件/残り人数")]
public class RemainingPlayersCondition : MinigameEndCondition
{
    [Tooltip("残りの人数がこれ以下になったら終わる。1 = 最後の1人になったら、0 = 全員脱落したら")]
    [SerializeField] private int remainingAtMost = 1;

    public override bool IsMet(MinigameBase game)
    {
        int remaining = 0;
        for (int i = 0; i < game.PlayerCount; i++)
        {
            if (IsActive(game.GetPlayer(i))) { remaining++; }
        }
        return remaining <= remainingAtMost;
    }
}
