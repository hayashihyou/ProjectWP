using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背景の上に重ねた白画像のアルファを動かして、一瞬光らせる。
/// </summary>
public class ButtonWhiteFlash : MonoBehaviour
{
    [Header("白フラッシュ(重ね画像)")]
    [Tooltip("白フラッシュを使うか")]
    [SerializeField] private bool useWhiteFlash = true;

    [Tooltip("背景の上に重ねる白い画像(普段は透明になります)")]
    [SerializeField] private Image flashOverlay;

    [Tooltip("フラッシュ時の最大の濃さ(0〜1)。小さいほど薄く光る")]
    [SerializeField, Range(0f, 1f)] private float flashMaxAlpha = 0.5f;

    [Tooltip("フラッシュの色")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("最大の濃さになるまでの秒数")]
    [SerializeField, Min(0f)] private float flashInDuration = 0.03f;

    [Tooltip("透明に戻るまでの秒数")]
    [SerializeField, Min(0f)] private float flashOutDuration = 0.15f;

    private Coroutine flashRoutine;

    private void Awake()
    {
        if (flashOverlay != null)
        {
            flashOverlay.raycastTarget = false;
            SetOverlayAlpha(0f);
        }
    }

    /// <summary>フラッシュを再生</summary>
    public void Play()
    {
        if (!useWhiteFlash || flashOverlay == null) return;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        SetOverlayAlpha(0f);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        // 透明 → 半透明の白 → 透明
        yield return Tween(flashInDuration, rate =>
            SetOverlayAlpha(Mathf.Lerp(0f, flashMaxAlpha, rate)));

        yield return Tween(flashOutDuration, rate =>
            SetOverlayAlpha(Mathf.Lerp(flashMaxAlpha, 0f, rate)));

        SetOverlayAlpha(0f);
        flashRoutine = null;
    }

    private void SetOverlayAlpha(float alpha)
    {
        flashOverlay.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
    }

    private static IEnumerator Tween(float duration, System.Action<float> onUpdate)
    {
        if (duration <= 0f) { onUpdate(1f); yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            onUpdate(Mathf.Clamp01(t / duration));
            yield return null;
        }
        onUpdate(1f);
    }
}