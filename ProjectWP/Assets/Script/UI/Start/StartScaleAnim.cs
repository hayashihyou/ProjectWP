using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 「スタート」文字のアニメーション。
/// 1) フェードインしながら 1.3 → 1.0 に縮小
/// 2) 少し表示をキープ
/// 3) 1.0 → 1.3 に拡大しながらフェードアウト
/// 指定したキーを押すと再生される(キーはプルダウンで選択)。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class StartScaleAnim : MonoBehaviour
{
    [Header("再生トリガー")]
    [Tooltip("キー入力で再生するか")]
    [SerializeField] private bool playOnKey = true;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    [Tooltip("再生に使うキー(新Input System)")]
    [SerializeField] private Key triggerKey = Key.Space;
#else
    [Tooltip("再生に使うキー(旧Input Manager)")]
    [SerializeField] private KeyCode triggerKey = KeyCode.Space;
#endif

    [Tooltip("開始時に自動で再生するか")]
    [SerializeField] private bool playOnStart = false;

    [Tooltip("アニメーション中にキーが押されても無視するか(OFFなら最初から再生し直す)")]
    [SerializeField] private bool ignoreKeyWhilePlaying = true;

    [Header("登場(フェードイン + 縮小)")]
    [Tooltip("登場開始時の倍率(ここから1.0に縮小)")]
    [SerializeField] private float appearStartScale = 1.3f;

    [Tooltip("縮小にかける秒数")]
    [SerializeField, Min(0f)] private float appearScaleDuration = 0.25f;

    [Tooltip("フェードインにかける秒数")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.25f;

    [Tooltip("縮小の動き方")]
    [SerializeField] private AnimationCurve appearCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("表示キープ")]
    [Tooltip("1.0の状態で文字を見せる秒数")]
    [SerializeField, Min(0f)] private float holdDuration = 0.5f;

    [Header("退場(拡大 + フェードアウト)")]
    [Tooltip("退場時に拡大する倍率(1.0からここまで拡大)")]
    [SerializeField] private float disappearEndScale = 1.3f;

    [Tooltip("拡大にかける秒数")]
    [SerializeField, Min(0f)] private float disappearScaleDuration = 0.25f;

    [Tooltip("フェードアウトにかける秒数")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.25f;

    [Tooltip("拡大の動き方")]
    [SerializeField] private AnimationCurve disappearCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("その他")]
    [Tooltip("再生前は非表示(透明)にしておくか")]
    [SerializeField] private bool hideOnAwake = true;

    [Tooltip("ポーズ中(Time.timeScale = 0)も動かすか")]
    [SerializeField] private bool useUnscaledTime = false;

    [Tooltip("退場完了後にオブジェクトを非アクティブにするか")]
    [SerializeField] private bool deactivateOnFinish = false;

    /// <summary>アニメーション完了時に呼ばれる</summary>
    public event System.Action OnFinished;

    private CanvasGroup canvasGroup;
    private Vector3 baseScale;
    private Coroutine running;

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        baseScale = transform.localScale;

        if (hideOnAwake) canvasGroup.alpha = 0f;
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    private void Update()
    {
        if (!playOnKey) return;
        if (ignoreKeyWhilePlaying && running != null) return;

        if (IsKeyPressedThisFrame()) Play();
    }

    private bool IsKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        if (triggerKey == Key.None) return false;
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[triggerKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(triggerKey);
#endif
    }

    /// <summary>アニメーションを最初から再生</summary>
    public void Play()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(AnimRoutine());
    }

    /// <summary>再生を止めて、透明・元の大きさに戻す</summary>
    public void ResetState()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        transform.localScale = baseScale;
        canvasGroup.alpha = 0f;
    }

    private IEnumerator AnimRoutine()
    {
        // 初期状態:透明、大きい状態から始める
        canvasGroup.alpha = 0f;
        transform.localScale = baseScale * appearStartScale;

        // 1. フェードイン + 縮小(それぞれの秒数が違っても同時に進む)
        yield return PlayParallel(
            appearScaleDuration, rate =>
                transform.localScale = baseScale * Mathf.LerpUnclamped(appearStartScale, 1f, appearCurve.Evaluate(rate)),
            fadeInDuration, rate =>
                canvasGroup.alpha = rate);

        transform.localScale = baseScale;
        canvasGroup.alpha = 1f;

        // 2. 表示キープ
        if (holdDuration > 0f) yield return Wait(holdDuration);

        // 3. 拡大 + フェードアウト
        yield return PlayParallel(
            disappearScaleDuration, rate =>
                transform.localScale = baseScale * Mathf.LerpUnclamped(1f, disappearEndScale, disappearCurve.Evaluate(rate)),
            fadeOutDuration, rate =>
                canvasGroup.alpha = 1f - rate);

        transform.localScale = baseScale * disappearEndScale;
        canvasGroup.alpha = 0f;

        running = null;
        OnFinished?.Invoke();

        if (deactivateOnFinish) gameObject.SetActive(false);
    }

    /// <summary>
    /// 2つの動き(拡縮とフェード)を同時に進める。
    /// 秒数が違う場合は、短い方が先に終わり、長い方が終わるまで待つ。
    /// </summary>
    private IEnumerator PlayParallel(
        float durationA, System.Action<float> onUpdateA,
        float durationB, System.Action<float> onUpdateB)
    {
        float total = Mathf.Max(durationA, durationB);
        float t = 0f;

        while (t < total)
        {
            t += DeltaTime;
            onUpdateA(durationA <= 0f ? 1f : Mathf.Clamp01(t / durationA));
            onUpdateB(durationB <= 0f ? 1f : Mathf.Clamp01(t / durationB));
            yield return null;
        }

        onUpdateA(1f);
        onUpdateB(1f);
    }

    private IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += DeltaTime;
            yield return null;
        }
    }
}