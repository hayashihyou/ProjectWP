using UnityEngine;


/// <summary>
/// タッチ操作UIの表示・非表示を制御するスクリプト
/// </summary>
public class TouchControlsVisibility : MonoBehaviour
{
    [Header("表示/非表示を切り替えたいUIの親オブジェクト(スティック一式)")]
    [SerializeField] private GameObject touchControlsRoot;

    void Start()
    {
        bool isTouchDevice = UnityEngine.InputSystem.Touchscreen.current != null;

        touchControlsRoot.SetActive(isTouchDevice);
    }
}
