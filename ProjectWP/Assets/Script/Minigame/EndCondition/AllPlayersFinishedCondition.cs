using UnityEngine;

/// <summary>
/// 終了条件: 全員が「このラウンドを終えた」(ServerSetFinished)になったら終わる。
/// 例: ストップウォッチで、全員が止めたら終わる。
/// 脱落した人と途中で抜けた人は数えない。
/// </summary>
[AddComponentMenu("Minigame/終了条件/全員が終えた")]
public class AllPlayersFinishedCondition : MinigameEndCondition
{
    public override bool IsMet(MinigameBase game)
    {
        for (int i = 0; i < game.PlayerCount; i++)
        {
            MinigamePlayerState player = game.GetPlayer(i);
            if (IsActive(player) && !player.IsFinished) { return false; }
        }
        return true;
    }
}
