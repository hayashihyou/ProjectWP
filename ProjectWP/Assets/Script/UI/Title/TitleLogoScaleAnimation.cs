using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// タイトルロゴのスケールアニメーション。
/// 0 → 1.5 → 1.0 のように、中心から拡大して落ち着く動きをします。
/// 各スケールはXYZ別々に設定できるので、左右だけ・上下だけの拡大も可能です。
/// ※ 中心から拡大させるため、RectTransformのPivotは (0.5, 0.5) にしてください。
/// </summary>
public class TitleLogoScaleAnimation : MonoBehaviour
{
    [Header("再生設定")]
    [Tooltip("Startで自動再生する（通常は演出制御スクリプトから再生するのでOFF）")]
    [SerializeField] private bool playOnStart = false;
    [Tooltip("Time.timeScale の影響を受けない")]
    [SerializeField] private bool useUnscaledTime = true;
    [Tooltip("Play()を呼んでから動き出すまでの待ち時間(秒)")]
    [SerializeField] private float startDelay = 0f;

    [Header("スケール (X, Y, Z)")]
    [Tooltip("開始時のスケール。X,Yを0にすると点から出てくる")]
    [SerializeField] private Vector3 startScale = new Vector3(0f, 0f, 1f);
    [Tooltip("一番大きくなるときのスケール。X・Yを別の値にすると左右/上下拡大になる")]
    [SerializeField] private Vector3 peakScale = new Vector3(1.5f, 1.5f, 1f);
    [Tooltip("最終的に落ち着くスケール")]
    [SerializeField] private Vector3 endScale = Vector3.one;

    [Header("時間 (秒)")]
    [Tooltip("開始 → ピーク までの時間")]
    [SerializeField] private float expandDuration = 0.4f;
    [Tooltip("ピーク → 最終 までの時間")]
    [SerializeField] private float settleDuration = 0.2f;

    [Header("イージング")]
    [SerializeField] private AnimationCurve expandCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve settleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("イベント")]
    public UnityEvent onFinished;

    public bool IsPlaying { get; private set; }

    private Coroutine routine;

    private void Awake()
    {
        // 再生前は見えない状態にしておく
        transform.localScale = startScale;
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>アニメーションを最初から再生</summary>
    public void Play()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(PlayRoutine());
    }

    /// <summary>アニメーションをスキップして最終スケールにする</summary>
    public void Skip()
    {
        if (routine != null) StopCoroutine(routine);
        transform.localScale = endScale;
        IsPlaying = false;
        routine = null;
        onFinished?.Invoke();
    }

    /// <summary>開始スケールに戻す</summary>
    public void ResetToStart()
    {
        if (routine != null) StopCoroutine(routine);
        transform.localScale = startScale;
        IsPlaying = false;
        routine = null;
    }

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        transform.localScale = startScale;

        if (startDelay > 0f)
            yield return Wait(startDelay);

        yield return ScaleTo(startScale, peakScale, expandDuration, expandCurve);
        yield return ScaleTo(peakScale, endScale, settleDuration, settleCurve);

        transform.localScale = endScale;
        IsPlaying = false;
        routine = null;
        onFinished?.Invoke();
    }

    private IEnumerator ScaleTo(Vector3 from, Vector3 to, float duration, AnimationCurve curve)
    {
        if (duration <= 0f)
        {
            transform.localScale = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += DeltaTime;
            float rate = curve.Evaluate(Mathf.Clamp01(t / duration));
            // カーブが0〜1をはみ出してもOKなようにUnclamped
            transform.localScale = Vector3.LerpUnclamped(from, to, rate);
            yield return null;
        }
        transform.localScale = to;
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