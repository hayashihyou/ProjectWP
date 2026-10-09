using UnityEngine;

/// <summary>
/// 終了条件: 値が目標に届いたら終わる。
/// 例: 誰か1人が10点取ったら終わる / 全員が5回成功したら終わる。
/// 脱落した人と途中で抜けた人は数えない。
/// </summary>
[AddComponentMenu("Minigame/終了条件/値が目標に届いた")]
public class ValueReachedCondition : MinigameEndCondition
{
    /// <summary>目標の値との比べ方</summary>
    public enum Comparison : byte
    {
        AtLeast, // 以上
        AtMost,  // 以下
    }

    /// <summary>誰の値を見るか</summary>
    public enum Target : byte
    {
        Anyone,   // 誰か1人でも届いたら終わる
        Everyone, // 全員が届いたら終わる
    }

    /// <summary>どの値を見るか</summary>
    public enum Source : byte
    {
        Round, // 今回のラウンドの値
        Total, // 合計の値
    }

    [Tooltip("目標の値")]
    [SerializeField] private float targetValue = 10f;

    [Tooltip("目標の値との比べ方。AtLeast = 以上、AtMost = 以下")]
    [SerializeField] private Comparison comparison = Comparison.AtLeast;

    [Tooltip("誰の値を見るか。Anyone = 誰か1人でも届いたら、Everyone = 全員が届いたら")]
    [SerializeField] private Target target = Target.Anyone;

    [Tooltip("どの値を見るか。Round = 今回のラウンドの値、Total = 合計の値")]
    [SerializeField] private Source source = Source.Round;

    public override bool IsMet(MinigameBase game)
    {
        bool anyoneReached = false;
        bool everyoneReached = true;
        bool hasActivePlayer = false;

        for (int i = 0; i < game.PlayerCount; i++)
        {
            MinigamePlayerState player = game.GetPlayer(i);
            if (!IsActive(player)) { continue; }

            hasActivePlayer = true;
            if (HasReached(player))
            {
                anyoneReached = true;
            }
            else
            {
                everyoneReached = false;
            }
        }

        // 数える人が1人もいないときは、この条件では終わらない(残り人数の条件に任せる)
        if (!hasActivePlayer) { return false; }

        return target == Target.Anyone ? anyoneReached : everyoneReached;
    }

    // この人の値が目標に届いているか
    private bool HasReached(MinigamePlayerState player)
    {
        float value = source == Source.Round ? player.RoundValue : player.TotalValue;
        return comparison == Comparison.AtLeast ? value >= targetValue : value <= targetValue;
    }
}
