using UnityEngine;

/// <summary>
/// 端末の種類(PC/タブレット/スマホ)に応じて、このUIの大きさに切り替える
/// </summary>
public class DeviceUiScale : MonoBehaviour
{
    [Header("端末ごとの倍率(1で元の大きさ)")]
    [SerializeField] private float pcScale = 1f;
    [SerializeField] private float tabletScale = 1.3f;
    [SerializeField] private float phoneScale = 1.8f;

    private void Awake()
    {
        float scale;
        switch(DeviceClassDetecter.Detect())
        {
            case DeviceClass.Phone:
                scale = phoneScale;
                break;

            case DeviceClass.Tablet:
                scale = tabletScale;
                break;

            default:
                scale = pcScale;
                break;
        }

        transform.localScale = new Vector3(scale, scale, 1f);
    }
}
