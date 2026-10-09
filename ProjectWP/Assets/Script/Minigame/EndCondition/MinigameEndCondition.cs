using UnityEngine;

/// <summary>
/// ラウンドの終了条件の基底クラス。MinigameBase と同じオブジェクトに Add Component で付ける。
/// 付けた条件のうち、どれか1つでも満たすとラウンドが終わる(制限時間は MinigameBase の設定)。
///
/// 新しい条件が必要になったら、これを継承して IsMet を書いたコンポーネントを1つ作れば足せる。
/// IsMet はサーバーで、プレイ中に毎フレーム呼ばれる。
/// </summary>
public abstract class MinigameEndCondition : MonoBehaviour
{
    /// <summary>条件を満たしていれば true を返す。game から全員の情報(PlayerCount / GetPlayer)を読める</summary>
    public abstract bool IsMet(MinigameBase game);

    /// <summary>
    /// 終了条件の人数に数える人か(脱落していない、かつ接続中)。
    /// 途中で抜けた人と脱落した人は、終了条件の人数に数えない(指示書4章のきまり)
    /// </summary>
    protected static bool IsActive(MinigamePlayerState player)
    {
        return player.IsConnected && !player.IsEliminated;
    }
}
