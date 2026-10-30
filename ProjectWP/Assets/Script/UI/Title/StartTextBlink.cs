using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 「はじめる」テキストのフェードイン → ゆっくり点滅 → キー入力でフェードアウト。
/// CanvasGroupのアルファを操作するので、TextMeshPro / Text / Image どれにでも使えます。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class StartTextBlink : MonoBehaviour
{
    [Header("再生設定")]
    [SerializeField] private bool playOnStart = false;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("アルファ")]
    [Range(0f, 1f)][SerializeField] private float maxAlpha = 1f;
    [Range(0f, 1f)][SerializeField] private float minAlpha = 0.15f;

    [Header("最初のフェードイン (秒)")]
    [SerializeField] private float firstFadeInDuration = 1.0f;

    [Header("点滅 (秒)")]
    [Tooltip("明 → 暗 になるまでの時間")]
    [SerializeField] private float blinkFadeOutDuration = 1.0f;
    [Tooltip("暗い状態で止まる時間")]
    [SerializeField] private float holdMinDuration = 0.1f;
    [Tooltip("暗 → 明 になるまでの時間")]
    [SerializeField] private float blinkFadeInDuration = 1.0f;
    [Tooltip("明るい状態で止まる時間")]
    [SerializeField] private float holdMaxDuration = 0.3f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("フェードアウト (キー入力)")]
    [Tooltip("プルダウンで選べます")]
    [SerializeField] private KeyCode exitKey = KeyCode.Return;
    [Tooltip("ONにすると何のキーでも反応する (上のキー指定は無視)")]
    [SerializeField] private bool useAnyKey = false;
    [Tooltip("OFFにすると、このスクリプトはキー入力を見ない。\n" +
         "外のスクリプトから StartExit() を呼んでフェードアウトさせる(タイトルシーンでは ServerListUI が呼ぶ)")]
    [SerializeField] private bool listenInput = true;
    [SerializeField] private float exitFadeDuration = 0.5f;

    [Header("イベント")]
    [Tooltip("フェードアウトが始まった瞬間")]
    public UnityEvent onExitStarted;
    [Tooltip("フェードアウトが完了した瞬間（シーン遷移などに）")]
    public UnityEvent onExitCompleted;

    public bool IsPlaying { get; private set; }
    public bool IsExiting { get; private set; }

    private CanvasGroup canvasGroup;
    private Coroutine routine;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f; // 再生前は非表示
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    private void Update()
    {
        // 入力を外に任せている場合は、ここではキーを見ない
        // (このプロジェクトは Input System だけを使う設定なので、古い Input を読むとエラーになる)
        if (!listenInput) return;
        if (!IsPlaying || IsExiting) return;

        bool pressed = useAnyKey ? Input.anyKeyDown : Input.GetKeyDown(exitKey);
        if (pressed) StartExit();
    }

    /// <summary>フェードイン → 点滅を開始</summary>
    public void Play()
    {
        if (routine != null) StopCoroutine(routine);
        IsExiting = false;
        IsPlaying = true;
        routine = StartCoroutine(BlinkRoutine());
    }

    /// <summary>フェードアウトを開始（キー入力以外からも呼べる）</summary>
    public void StartExit()
    {
        if (!IsPlaying || IsExiting) return;
        if (routine != null) StopCoroutine(routine);
        IsExiting = true;
        onExitStarted?.Invoke();
        routine = StartCoroutine(ExitRoutine());
    }

    /// <summary>即座に非表示にして停止</summary>
    public void HideImmediately()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        IsPlaying = false;
        IsExiting = false;
        canvasGroup.alpha = 0f;
    }

    private IEnumerator BlinkRoutine()
    {
        // 最初のフェードイン
        yield return Fade(0f, maxAlpha, firstFadeInDuration);

        // 点滅ループ
        while (true)
        {
            yield return Fade(maxAlpha, minAlpha, blinkFadeOutDuration);
            if (holdMinDuration > 0f) yield return Wait(holdMinDuration);

            yield return Fade(minAlpha, maxAlpha, blinkFadeInDuration);
            if (holdMaxDuration > 0f) yield return Wait(holdMaxDuration);
        }
    }

    private IEnumerator ExitRoutine()
    {
        // 今のアルファから0へ
        yield return Fade(canvasGroup.alpha, 0f, exitFadeDuration);

        canvasGroup.alpha = 0f;
        IsPlaying = false;
        IsExiting = false;
        routine = null;
        onExitCompleted?.Invoke();
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            canvasGroup.alpha = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += DeltaTime;
            float rate = fadeCurve.Evaluate(Mathf.Clamp01(t / duration));
            canvasGroup.alpha = Mathf.Lerp(from, to, rate);
            yield return null;
        }
        canvasGroup.alpha = to;
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

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
}