using UnityEngine;


/// <summary>
/// 端末の種類(PC/タブレット/スマホ)に応じて、このUIの位置を切り替える
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DeviceUiPosition : MonoBehaviour
{
    [Header("端末ごとの位置(Rect Transformの　PosX/PosY　に入る値)")]
    [SerializeField] private Vector2 pcPosition;
    [SerializeField] private Vector2 tabletPosition;
    [SerializeField] private Vector2 phonePosition;


    // 
    private void Awake()
    {
        Vector2 position;
        switch(DeviceClassDetecter.Detect())
        {
            case DeviceClass.Phone:
                position = phonePosition;
                break;

            case DeviceClass.Tablet:
                position = tabletPosition;
                break;

            default:
                position = pcPosition;
                break;
        }

        ((RectTransform)transform).anchoredPosition = position;
    }
}
