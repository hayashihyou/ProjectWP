using NUnit.Framework.Internal.Commands;
using UnityEngine;

/// <summary>
/// 画面が基準(16:9)より縦長寄りの時、カメラの視野角を広げる
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraAspectFitter : MonoBehaviour
{
    [Header("画面作りの基準にしている縦横比(横÷縦)")]
    [SerializeField] private float referenceAspect = 16f / 9f;

    private Camera targetCamera;
    private float referenceVerticalFov; // シーンに設定されている元の視野角
    private float lastAspect = -1f;


    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
        referenceVerticalFov = targetCamera.fieldOfView;
        Apply();
    }


    private void Update()
    {
        if (!Mathf.Approximately(targetCamera.aspect, lastAspect)) Apply();
    }


    private void Apply()
    {
        float aspect = targetCamera.aspect;
        lastAspect = aspect;
        if (aspect <= 0f) return;

        if (aspect >= referenceAspect)
        {
            targetCamera.fieldOfView = referenceVerticalFov;
            return;
        }

        float halfwidth = Mathf.Tan(referenceVerticalFov * 0.5f * Mathf.Deg2Rad) * referenceAspect;
        targetCamera.fieldOfView = Mathf.Atan(halfwidth / aspect) * 2f * Mathf.Rad2Deg;
    }
}
