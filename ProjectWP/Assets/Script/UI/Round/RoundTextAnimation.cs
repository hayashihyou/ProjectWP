using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 「ROUND 1」などのテキストに、拡縮とフェードのアニメーションを付けるスクリプト。
/// 
/// 動き:
///   1. フェードイン(0→1)しながら、スケール 1.3 → 1.0 に縮小
///   2. 指定秒数だけ表示を維持
///   3. フェードアウト(1→0)しながら、スケール 1.0 → 1.3 に拡大
///
/// 使い方:
///   - 文字を表示するオブジェクト(TextMeshPro や UI Text、画像など)に付ける
///   - CanvasGroup が無ければ自動で追加される(アルファ制御用)
///   - Inspector で大きさや時間を調整できる
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
    [Tooltip("スペースキーで再生する")]
    [SerializeField] private bool playOnSpaceKey = true;

    [Tooltip("再生中にスペースキーを押した場合、最初からやり直す(OFFなら再生中は無視)")]
    [SerializeField] private bool restartWhilePlaying = true;

    [Tooltip("有効になった時に自動で再生する")]
    [SerializeField] private bool playOnEnable = false;

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

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        ResetState();
    }

    private void Update()
    {
        if (!playOnSpaceKey) return;
        if (!IsSpacePressedThisFrame()) return;
        if (currentRoutine != null && !restartWhilePlaying) return;

        Play();
    }

    private static bool IsSpacePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        // 新しい Input System
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        // 従来の Input Manager
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private void OnEnable()
    {
        if (playOnEnable)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        if (currentRoutine != null)
        {
            StopCoroutine(currentRoutine);
            currentRoutine = null;
        }
    }

    /// <summary>アニメーションを最初から再生する。</summary>
    public void Play()
    {
        if (currentRoutine != null)
        {
            StopCoroutine(currentRoutine);
        }
        currentRoutine = StartCoroutine(AnimationRoutine());
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