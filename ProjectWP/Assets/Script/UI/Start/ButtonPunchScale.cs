using System.Collections;
using UnityEngine;

/// <summary>
/// ボタンを 1.0 → ピーク倍率 → 1.0 と拡縮する。
/// ピーク倍率は1未満(縮む方向)にも設定可能。
/// </summary>
public class ButtonPunchScale : MonoBehaviour
{
    [Header("拡縮")]
    [Tooltip("ピーク時の倍率。1.3で膨らむ、0.8などで縮む")]
    [SerializeField] private float peakScale = 1.3f;

    [Tooltip("1.0 → ピークまでの秒数")]
    [SerializeField, Min(0.01f)] private float scaleUpDuration = 0.08f;

    [Tooltip("ピーク → 1.0 までの秒数")]
    [SerializeField, Min(0.01f)] private float scaleDownDuration = 0.12f;

    [Tooltip("拡縮の動き方(0〜1の入力に対する補間カーブ)")]
    [SerializeField] private AnimationCurve easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Vector3 baseScale;
    private Coroutine scaleRoutine;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    /// <summary>拡縮を再生</summary>
    public void Play()
    {
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        transform.localScale = baseScale;
        scaleRoutine = StartCoroutine(ScaleRoutine());
    }

    private IEnumerator ScaleRoutine()
    {
        yield return Tween(scaleUpDuration, rate =>
            transform.localScale = baseScale * Mathf.LerpUnclamped(1f, peakScale, easeCurve.Evaluate(rate)));

        yield return Tween(scaleDownDuration, rate =>
            transform.localScale = baseScale * Mathf.LerpUnclamped(peakScale, 1f, easeCurve.Evaluate(rate)));

        transform.localScale = baseScale;
        scaleRoutine = null;
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