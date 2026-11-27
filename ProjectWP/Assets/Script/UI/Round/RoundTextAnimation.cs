using System.Collections;
using UnityEngine;

/// <summary>
/// 「ROUND 1」などのテキストに、拡縮とフェードのアニメーションを付けるスクリプト。
///
/// 動き:
///   1. フェードイン(0→1)しながら、スケール 1.3 → 1.0 に縮小
///   2. 指定秒数だけ表示を維持
///   3. フェードアウト(1→0)しながら、スケール 1.0 → 1.3 に拡大
///
/// 使い方:
///   - 文字と点をまとめた親オブジェクトに付ける(CanvasGroup が自動で追加される)
///   - 親ごとフェード・拡縮するので、中の点も一緒に動く
///   - スペースキーでの再生や、点との連動は RoundAnimationController が担当する
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class RoundTextAnimation : MonoBehaviour
{
    [Header("スケール設定")]
    [Tooltip("登場時の開始スケール / 退場時の終了スケール")]
    [SerializeField] private float largeScale = 1.3f;

    [Tooltip("表示中の基本スケール")]
    [SerializeField] private float normalScale = 1.0f;

    [Header("時間設定(秒)")]
    [Tooltip("フェードインしながら縮小する時間")]
    [SerializeField] private float fadeInDuration = 0.5f;

    [Tooltip("表示を維持する時間")]
    [SerializeField] private float holdDuration = 2.0f;

    [Tooltip("フェードアウトしながら拡大する時間")]
    [SerializeField] private float fadeOutDuration = 0.5f;

    [Header("オプション")]
    [Tooltip("終了後にオブジェクトを非アクティブにする")]
    [SerializeField] private bool deactivateOnFinish = false;

    [Tooltip("Time.timeScale の影響を受けない(ポーズ中でも動かしたい場合)")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("イージング")]
    [Tooltip("登場時のカーブ(0〜1)")]
    [SerializeField] private AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("退場時のカーブ(0〜1)")]
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private CanvasGroup canvasGroup;
    private Coroutine currentRoutine;

    /// <summary>アニメーション再生中かどうか。</summary>
    public bool IsPlaying => currentRoutine != null;

    /// <summary>現在の透明度(0〜1)。点のフェードをこれに合わせるために使う。</summary>
    public float Alpha => canvasGroup != null ? canvasGroup.alpha : 0f;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        ResetState();
    }

    private void OnDisable()
    {
        Stop();
    }

    /// <summary>アニメーションを最初から再生する。</summary>
    public void Play()
    {
        Stop();
        currentRoutine = StartCoroutine(AnimationRoutine());
    }

    /// <summary>再生を止める(見た目はそのまま)。</summary>
    public void Stop()
    {
        if (currentRoutine != null)
        {
            StopCoroutine(currentRoutine);
            currentRoutine = null;
        }
    }

    /// <summary>非表示状態(透明・拡大スケール)に戻す。</summary>
    public void ResetState()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        transform.localScale = Vector3.one * largeScale;
    }

    private IEnumerator AnimationRoutine()
    {
        // 1. フェードイン + 縮小(1.3 → 1.0)
        yield return Animate(fadeInDuration, fadeInCurve, t =>
        {
            canvasGroup.alpha = t;
            transform.localScale = Vector3.one * Mathf.Lerp(largeScale, normalScale, t);
        });

        // 2. 表示維持
        canvasGroup.alpha = 1f;
        transform.localScale = Vector3.one * normalScale;
        yield return Wait(holdDuration);

        // 3. フェードアウト + 拡大(1.0 → 1.3)
        yield return Animate(fadeOutDuration, fadeOutCurve, t =>
        {
            canvasGroup.alpha = 1f - t;
            transform.localScale = Vector3.one * Mathf.Lerp(normalScale, largeScale, t);
        });

        canvasGroup.alpha = 0f;
        transform.localScale = Vector3.one * largeScale;
        currentRoutine = null;

        if (deactivateOnFinish)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>0→1 の進行度 t を curve 経由で onUpdate に渡す。</summary>
    private IEnumerator Animate(float duration, AnimationCurve curve, System.Action<float> onUpdate)
    {
        if (duration <= 0f)
        {
            onUpdate(1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            onUpdate(curve.Evaluate(t));
            yield return null;
        }
        onUpdate(1f);
    }

    private IEnumerator Wait(float seconds)
    {
        if (seconds <= 0f) yield break;

        if (useUnscaledTime)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }
        else
        {
            yield return new WaitForSeconds(seconds);
        }
    }
}