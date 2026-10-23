using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 「拡大縮小+フェードイン → 光る → 拡大+フェードアウト」を再生する汎用演出スクリプト。
/// TextMeshProUGUI も Image も Graphic を継承しているので、同じスクリプトで両方動きます。
/// </summary>
public class FinishEffect : MonoBehaviour
{
    [Header("対象 (TMP でも Image でもOK)")]
    [SerializeField] private Graphic target;

    [Header("再生トリガー")]
    [SerializeField] private bool playOnKey = true;
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key triggerKey = Key.Space;
#else
    [SerializeField] private KeyCode triggerKey = KeyCode.Space;
#endif
    [SerializeField] private bool playOnStart = false;
    [SerializeField] private bool hideOnAwake = true;
    [Tooltip("ONにするとTime.timeScale=0でも動きます")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("1. 登場 (拡大率 → 1.0 + フェードイン)")]
    [SerializeField] private bool useScaleIn = true;
    [SerializeField] private float inStartScale = 1.5f;
    [SerializeField] private bool useFadeIn = true;
    [SerializeField] private float inDuration = 0.4f;
    [SerializeField] private AnimationCurve inCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("2. 光る演出")]
    [SerializeField] private bool useGlow = true;
    [Tooltip("フェードイン完了から、何秒後に光り始めるか")]
    [SerializeField] private float glowDelay = 0f;
    [Tooltip("文字色に混ぜる色。通常は白")]
    [SerializeField] private Color glowColor = Color.white;
    [Range(0f, 1f)]
    [SerializeField] private float glowIntensity = 1f;
    [SerializeField] private float glowDuration = 0.4f;
    [Tooltip("ONにすると、光る演出を繰り返します。\nフェードアウトを使う場合は Fade Out Delay の時間まで繰り返し、\n使わない場合は止めるまで繰り返します。")]
    [SerializeField] private bool glowLoop = false;
    [Tooltip("ループ時、1回光ってから次に光るまでの待ち時間(秒)")]
    [SerializeField] private float glowLoopInterval = 0.3f;
    [Tooltip("0→1→0 の山形カーブ (横軸=時間, 縦軸=白の混ざり具合)")]
    [SerializeField]
    private AnimationCurve glowCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

    [Header("3. 退場 (1.0 → 拡大率 + フェードアウト)")]
    [SerializeField] private bool useOut = true;
    [Tooltip("フェードイン完了から、何秒後にフェードアウトを始めるか。\n光る演出(待ち時間+光る時間)もこの中に含みます。\nそれより短い場合は、光り終わってすぐ始まります。")]
    [SerializeField] private float fadeOutDelay = 1.0f;
    [SerializeField] private bool useScaleOut = true;
    [SerializeField] private float outEndScale = 1.5f;
    [SerializeField] private bool useFadeOut = true;
    [SerializeField] private float outDuration = 0.4f;
    [SerializeField] private AnimationCurve outCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("退場後にGameObjectを非アクティブにする")]
    [SerializeField] private bool deactivateAfterOut = false;

    private Color baseColor;
    private Vector3 baseScale;
    private Coroutine running;

    // TMP の Vertex Gradient 対応用
    private TMP_Text tmp;
    private bool hasGradient;
    private VertexGradient baseGradient;

    private void Reset()
    {
        target = GetComponent<Graphic>();
    }

    private void Awake()
    {
        if (target == null) target = GetComponent<Graphic>();
        if (target == null)
        {
            Debug.LogError($"{nameof(FinishEffect)}: target (TMP/Image) が設定されていません。", this);
            enabled = false;
            return;
        }

        // インスペクタ上で設定されている色・スケールを「基準」として覚える
        baseColor = target.color;
        baseScale = target.rectTransform.localScale;

        tmp = target as TMP_Text;
        if (tmp != null && tmp.enableVertexGradient)
        {
            hasGradient = true;
            baseGradient = tmp.colorGradient;
        }

        if (hideOnAwake) Apply(1f, 0f, 0f);
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    private void Update()
    {
        if (!playOnKey) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current[triggerKey].wasPressedThisFrame) Play();
#else
        if (Input.GetKeyDown(triggerKey)) Play();
#endif
    }

    /// <summary>外部(ゲーム終了処理など)から呼べます。</summary>
    public void Play()
    {
        if (target == null) return;
        if (running != null) StopCoroutine(running);
        gameObject.SetActive(true);
        running = StartCoroutine(Sequence());
    }

    /// <summary>即座に非表示にします。</summary>
    public void HideImmediately()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        Apply(1f, 0f, 0f);
    }

    private IEnumerator Sequence()
    {
        // 1. 登場
        yield return Animate(inDuration, inCurve, t =>
        {
            float scale = useScaleIn ? Mathf.LerpUnclamped(inStartScale, 1f, t) : 1f;
            float alpha = useFadeIn ? t : 1f;
            Apply(scale, alpha, 0f);
        });
        Apply(1f, 1f, 0f);

        // ここが「フェードイン完了」。ここから fadeOutDelay 秒を数える
        float t0 = Now();

        // 2. 光る (フェードイン完了から glowDelay 秒後に開始)
        if (useGlow)
        {
            if (glowDelay > 0f) yield return Wait(glowDelay);

            while (true)
            {
                yield return Animate(glowDuration, null, t =>
                {
                    float g = glowCurve.Evaluate(t) * glowIntensity;
                    Apply(1f, 1f, g);
                });
                Apply(1f, 1f, 0f);

                if (!glowLoop) break;

                // 次の光までの待ち時間 (フェードアウト開始時刻を超えないようにする)
                float interval = glowLoopInterval;
                if (useOut) interval = Mathf.Min(interval, fadeOutDelay - (Now() - t0));
                if (interval > 0f) yield return Wait(interval);

                if (useOut && Now() - t0 >= fadeOutDelay) break;

                // 待ち時間も光る時間も0の場合の無限ループ防止
                if (glowDuration + glowLoopInterval <= 0f) yield return null;
            }
        }

        float waitRemain = fadeOutDelay - (Now() - t0);

        // 3. 退場
        if (useOut)
        {
            if (waitRemain > 0f) yield return Wait(waitRemain);

            if (useScaleOut || useFadeOut)
            {
                yield return Animate(outDuration, outCurve, t =>
                {
                    float scale = useScaleOut ? Mathf.LerpUnclamped(1f, outEndScale, t) : 1f;
                    float alpha = useFadeOut ? 1f - t : 1f;
                    Apply(scale, alpha, 0f);
                });
                float finalScale = useScaleOut ? outEndScale : 1f;
                float finalAlpha = useFadeOut ? 0f : 1f;
                Apply(finalScale, finalAlpha, 0f);
            }

            if (deactivateAfterOut)
            {
                running = null;
                gameObject.SetActive(false);
                yield break;
            }
        }

        running = null;
    }

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
            elapsed += DeltaTime();
            float t = Mathf.Clamp01(elapsed / duration);
            onUpdate(curve != null ? curve.Evaluate(t) : t);
            yield return null;
        }
    }

    private IEnumerator Wait(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += DeltaTime();
            yield return null;
        }
    }

    private float Now() => useUnscaledTime ? Time.unscaledTime : Time.time;

    private float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    /// <param name="scaleMul">基準スケールに掛ける倍率</param>
    /// <param name="alphaMul">基準アルファに掛ける倍率</param>
    /// <param name="glow">0〜1: glowColor をどれだけ混ぜるか</param>
    private void Apply(float scaleMul, float alphaMul, float glow)
    {
        target.rectTransform.localScale = baseScale * scaleMul;

        Color c = Color.Lerp(baseColor, glowColor, glow);
        c.a = baseColor.a * alphaMul;
        target.color = c;

        // TMP の Color Gradient を使っている場合は、グラデーション側の色も白に寄せる
        if (hasGradient)
        {
            tmp.colorGradient = new VertexGradient(
                Color.Lerp(baseGradient.topLeft, glowColor, glow),
                Color.Lerp(baseGradient.topRight, glowColor, glow),
                Color.Lerp(baseGradient.bottomLeft, glowColor, glow),
                Color.Lerp(baseGradient.bottomRight, glowColor, glow));
        }
    }
}