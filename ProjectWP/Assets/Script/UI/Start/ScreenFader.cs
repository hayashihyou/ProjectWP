using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CanvasGroup のαを動かして画面をフェードアウト/フェードインする。
/// NotifyReady() で「準備OK」を集計し、規定数に達したらフェードを開始できる。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    public enum FadeType { FadeOut, FadeIn }

    [Header("フェード設定")]
    [SerializeField] private FadeType fadeType = FadeType.FadeOut;

    [Tooltip("再生指示から何秒後にフェードを開始するか")]
    [SerializeField, Min(0f)] private float delay = 2f;

    [Tooltip("フェードにかける秒数")]
    [SerializeField, Min(0f)] private float duration = 0.5f;

    [Tooltip("フェード中にタッチを無効化するか")]
    [SerializeField] private bool blockRaycastsWhileFading = true;

    [Header("準備完了の条件(別々のボタンモード用)")]
    [Tooltip("この数だけ NotifyReady されたらフェードを開始する")]
    [SerializeField, Min(1)] private int requiredReadyCount = 4;

    [Header("オプション")]
    [Tooltip("フェードアウト完了後にオブジェクトを非アクティブにする")]
    [SerializeField] private bool deactivateOnFadeOut = false;

    [Tooltip("開始時に自動で再生する(条件を無視して再生されるので通常はオフ)")]
    [SerializeField] private bool playOnStart = false;

    [Tooltip("開始時の状態を fadeType に合わせる(FadeInなら透明から始める)")]
    [SerializeField] private bool setInitialAlpha = true;

    public event System.Action OnFadeCompleted;

    private CanvasGroup canvasGroup;
    private Coroutine running;
    private bool isFading; // 待ち時間が終わって実際にフェード中か

    // 誰がOKになったかを記録(同じ相手が2回通知しても二重カウントされない)
    private readonly HashSet<Object> readySources = new HashSet<Object>();

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (setInitialAlpha)
            canvasGroup.alpha = (fadeType == FadeType.FadeOut) ? 1f : 0f;
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>「準備OK」を通知。全員揃ったらフェード開始</summary>
    public void NotifyReady(Object source)
    {
        readySources.Add(source);
        if (readySources.Count >= requiredReadyCount && running == null)
            Play();
    }

    /// <summary>「準備OK」を取り消す。待ち時間中なら予約もキャンセル</summary>
    public void NotifyUnready(Object source)
    {
        readySources.Remove(source);
        if (readySources.Count < requiredReadyCount && running != null && !isFading)
        {
            StopCoroutine(running);
            running = null;
        }
    }

    public void ResetReady()
    {
        readySources.Clear();
    }

    /// <summary>条件を無視して再生</summary>
    public void Play()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FadeRoutine());
    }

    /// <summary>コードから種類・待ち時間を上書きして再生</summary>
    public void Play(FadeType type, float delaySeconds)
    {
        fadeType = type;
        delay = delaySeconds;
        Play();
    }

    private IEnumerator FadeRoutine()
    {
        isFading = false;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        isFading = true;
        float from = (fadeType == FadeType.FadeOut) ? 1f : 0f;
        float to = (fadeType == FadeType.FadeOut) ? 0f : 1f;

        canvasGroup.alpha = from;
        if (blockRaycastsWhileFading) canvasGroup.blocksRaycasts = false;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        canvasGroup.alpha = to;

        if (blockRaycastsWhileFading) canvasGroup.blocksRaycasts = (to > 0f);
        if (fadeType == FadeType.FadeOut && deactivateOnFadeOut) gameObject.SetActive(false);

        isFading = false;
        running = null;
        OnFadeCompleted?.Invoke();
    }
}