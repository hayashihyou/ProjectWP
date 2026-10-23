using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 光る演出(フラッシュ)だけを行う汎用スクリプト。
/// TextMeshProUGUI も Image も Graphic を継承しているので、同じスクリプトで両方動きます。
/// </summary>
public class TextFlashAnim : MonoBehaviour
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
    [Tooltip("ONにするとTime.timeScale=0でも動きます")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("光る演出")]
    [Tooltip("再生してから、何秒後に光り始めるか")]
    [SerializeField] private float glowDelay = 0f;
    [Tooltip("文字色に混ぜる色。通常は白")]
    [SerializeField] private Color glowColor = Color.white;
    [Range(0f, 1f)]
    [SerializeField] private float glowIntensity = 1f;
    [SerializeField] private float glowDuration = 0.4f;
    [Tooltip("ONにすると、光る演出を繰り返します。止めるときは Stop() を呼びます。")]
    [SerializeField] private bool glowLoop = false;
    [Tooltip("ループ時、1回光ってから次に光るまでの待ち時間(秒)")]
    [SerializeField] private float glowLoopInterval = 0.3f;
    [Tooltip("0→1→0 の山形カーブ (横軸=時間, 縦軸=白の混ざり具合)")]
    [SerializeField]
    private AnimationCurve glowCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

    private Color baseColor;
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
            Debug.LogError($"{nameof(TextFlashAnim)}: target (TMP/Image) が設定されていません。", this);
            enabled = false;
            return;
        }

        // インスペクタ上で設定されている色を「基準」として覚える
        baseColor = target.color;

        tmp = target as TMP_Text;
        if (tmp != null && tmp.enableVertexGradient)
        {
            hasGradient = true;
            baseGradient = tmp.colorGradient;
        }
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

    private void OnDisable()
    {
        // 非アクティブ時にコルーチンが止まるので、色を元に戻しておく
        running = null;
        if (target != null) Apply(0f);
    }

    /// <summary>光る演出を再生します。</summary>
    public void Play()
    {
        if (target == null) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Sequence());
    }

    /// <summary>光る演出を止めて、元の色に戻します。</summary>
    public void Stop()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        Apply(0f);
    }

    private IEnumerator Sequence()
    {
        if (glowDelay > 0f) yield return Wait(glowDelay);

        while (true)
        {
            yield return Animate(glowDuration, t =>
            {
                float g = glowCurve.Evaluate(t) * glowIntensity;
                Apply(g);
            });
            Apply(0f);

            if (!glowLoop) break;

            if (glowLoopInterval > 0f) yield return Wait(glowLoopInterval);

            // 待ち時間も光る時間も0の場合の無限ループ防止
            if (glowDuration + glowLoopInterval <= 0f) yield return null;
        }

        running = null;
    }

    private IEnumerator Animate(float duration, System.Action<float> onUpdate)
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
            onUpdate(Mathf.Clamp01(elapsed / duration));
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

    private float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    /// <param name="glow">0〜1: glowColor をどれだけ混ぜるか</param>
    private void Apply(float glow)
    {
        // アルファは現在の値を保つ (他のフェード演出と併用できるように)
        float currentAlpha = target.color.a;
        Color c = Color.Lerp(baseColor, glowColor, glow);
        c.a = currentAlpha;
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