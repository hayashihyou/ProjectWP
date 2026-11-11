using UnityEngine;

/// <summary>
/// 待機所の「参加したときの演出」1種類分の共通の土台。
/// 煙ポン(PlayerJoinSmokePop)などの演出は、すべてこれを継承する。
///
/// PlayerJoinPresentation と同じGameObjectに付けておくと、参加の瞬間に
/// 「有効(チェックが入っている)」な演出がすべて呼ばれる。
/// 演出を切り替えたい/組み合わせたいときは、インスペクターのチェックを付け外しするだけでよい。
/// 新しい演出を足すときも、これを継承したクラスを作って同じGameObjectに付けるだけでよい。
/// </summary>
public abstract class PlayerJoinPresenterBase : MonoBehaviour
{
    /// <summary>
    /// 参加演出を再生する。
    /// </summary>
    /// <param name="slotIndex">参加した枠(0=1P, 1=2P, ...)</param>
    /// <param name="color">演出に使う色(枠の色。白は見えやすい色に置き換え済み)</param>
    /// <param name="slot">参加したキャラクター。テストで「キャラがいない枠」を再生したときはnull</param>
    public abstract void Present(int slotIndex, Color color, PlayerSlot slot);
}
