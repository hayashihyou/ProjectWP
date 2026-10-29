using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// タイトル演出の進行管理。
/// ① ロゴのスケールアニメーション
/// ② 少しディレイ
/// ③ 「はじめる」が出てきて点滅
/// ④ 指定キーでフェードアウト → 完了イベント
/// </summary>
public class TitleSequenceController : MonoBehaviour
{
    [Header("対象")]
    [SerializeField] private TitleLogoScaleAnimation logoAnimation;
    [SerializeField] private StartTextBlink startTextBlink;

    [Header("設定")]
    [SerializeField] private bool playOnStart = true;
    [Tooltip("ロゴ演出が終わってから「はじめる」が出るまでの待ち時間(秒)")]
    [SerializeField] private float delayAfterLogo = 0.5f;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("イベント")]
    [Tooltip("「はじめる」のフェードアウト完了時（シーン遷移などをここに登録）")]
    public UnityEvent onSequenceCompleted;

    private Coroutine routine;

    private void Start()
    {
        if (startTextBlink != null)
            startTextBlink.onExitCompleted.AddListener(HandleExitCompleted);

        if (playOnStart) Play();
    }

    private void OnDestroy()
    {
        if (startTextBlink != null)
            startTextBlink.onExitCompleted.RemoveListener(HandleExitCompleted);
    }

    public void Play()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(SequenceRoutine());
    }

    private IEnumerator SequenceRoutine()
    {
        // ① ロゴ演出
        if (logoAnimation != null)
        {
            logoAnimation.Play();
            // Play()直後はIsPlayingが立つので、終わるまで待つ
            yield return new WaitUntil(() => !logoAnimation.IsPlaying);
        }

        // ② ディレイ
        float t = 0f;
        while (t < delayAfterLogo)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }

        // ③ 「はじめる」フェードイン＆点滅
        if (startTextBlink != null)
            startTextBlink.Play();

        routine = null;
    }

    private void HandleExitCompleted()
    {
        onSequenceCompleted?.Invoke();
    }
}