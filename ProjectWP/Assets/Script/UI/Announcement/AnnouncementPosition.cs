using UnityEngine;

/// <summary>
/// このシーンでのアナウンス(Announcement)を出す位置。シーンの好きなオブジェクトに Add Component で付ける。
/// 付けていないシーンでは、画面の下の中央に出る
/// </summary>
[AddComponentMenu("UI/アナウンスの位置")]
public class AnnouncementPosition : MonoBehaviour
{
    [Tooltip("帯の中心の位置(画面の割合)。X: 0 = 左端 / 1 = 右端、Y: 0 = 下端 / 1 = 上端。標準は (0.5, 0.07) = 下の中央")]
    [SerializeField] private Vector2 viewportPosition = new Vector2(0.5f, 0.07f);

    /// <summary>帯の中心の位置(画面の割合)</summary>
    public Vector2 ViewportPosition => viewportPosition;
}
