using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 上から下へ動きながらフェードインする汎用スクリプト。
/// TextMeshProUGUI も Image も Graphic を継承しているので、同じスクリプトで両方動きます。
/// 最終位置は、シーン上で置いた位置です。
/// </summary>
public class SlideDownFadeIn : MonoBehaviour
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

    [Header("上から下への移動")]
    [SerializeField] private bool useMove = true;
    [Tooltip("開始位置の、最終位置からのずれ (UI座標)。Y=+100 なら100上から降りてきます。")]
    [SerializeField] private Vector2 startOffset = new Vector2(0f, 100f);

    [Header("フェードイン")]
    [SerializeField] private bool useFadeIn = true;

    [Header("共通")]
    [Tooltip("再生してから、何秒後に始めるか")]
    [SerializeField] private float delay = 0f;
    [SerializeField] private float duration = 0.5f;
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Vector2 basePosition;
    private Color baseColor;
    private RectTransform rect;
    private Coroutine running;

    private void Reset()
    {
        target = GetComponent<Graphic>();
    }

    private void Awake()
    {
        if (target == null) target = GetComponent<Graphic>();
        if (target == null)
        {
            Debug.LogError($"{nameof(SlideDownFadeIn)}: target (TMP/Image) が設定されていません。", this);
            enabled = false;
            return;
        }

        rect = target.rectTransform;
        basePosition = rect.anchoredPosition;
        baseColor = target.color;

        if (hideOnAwake) Apply(0f);
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

    /// <summary>外部から呼べます。</summary>
    public void Play()
    {
        if (target == null) return;
        if (running != null) StopCoroutine(running);
        gameObject.SetActive(true);
        running = StartCoroutine(Sequence());
    }

    /// <summary>即座に非表示(開始状態)に戻します。</summary>
    public void HideImmediately()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        Apply(0f);
    }

    private IEnumerator Sequence()
    {
        // 再生前に開始状態にしておく
        Apply(0f);

        float elapsed = 0f;
        while (elapsed < delay)
        {
            elapsed += DeltaTime();
            yield return null;
        }

        if (duration > 0f)
        {
            elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += DeltaTime();
                Apply(curve.Evaluate(Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
        }

        Apply(1f);
        running = null;
    }

    private float DeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    /// <param name="t">0=開始状態(上にいて透明), 1=最終状態(元の位置で不透明)</param>
    private void Apply(float t)
    {
        rect.anchoredPosition = useMove
            ? Vector2.LerpUnclamped(basePosition + startOffset, basePosition, t)
            : basePosition;

        Color c = baseColor;
        c.a = baseColor.a * (useFadeIn ? Mathf.Clamp01(t) : 1f);
        target.color = c;
    }
}