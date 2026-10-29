using System.Collections;
using UnityEngine;

/// <summary>
/// 2ラウンド目からの、目標時間を見せるカメラの演出。Main Camera(MinigameIntroCamera と同じオブジェクト)に付ける。
/// (1ラウンド目は、最初のイントロ演出 MinigameIntroCamera がテレビを映すので、ここでは何もしない)
///
/// 流れ
///   画面が暗い間(RoundReset)   → テレビに寄った位置へ一瞬で移る
///   明るくなる(RoundIntro)     → テレビに寄った画面のまま
///   「目標決定」の出来事        → holdSeconds 秒そのまま見せてから、returnSeconds 秒かけて元の位置へ戻る
///
/// サーバーの StopwatchMinigame は、TargetDelaySeconds と SecondsAfterTarget を読んで待つ時間を決める。
/// 秒数を変えるときは、ここ(このコンポーネント)だけを変えればよい。
/// </summary>
public class StopwatchRoundCamera : MonoBehaviour
{
    [Header("映すもの")]
    [Tooltip("テレビの画面の中心(StopwatchTV の ScreenAnchor)")]
    [SerializeField] private Transform screen;

    [Tooltip("テレビからどれだけ離れて映すか(小さいほど大きく映る)")]
    [SerializeField] private float zoomDistance = 5.0f;

    [Header("時間(秒)")]
    [Tooltip("明るくなってから、テレビに目標時間が出るまでの間")]
    [SerializeField] private float targetDelaySeconds = 0.4f;

    [Tooltip("目標時間が出てから、テレビを映したまま止まっている時間")]
    [SerializeField] private float holdSeconds = 1.0f;

    [Tooltip("元の位置に戻るまでの時間")]
    [SerializeField] private float returnSeconds = 1.5f;

    // 戻る先(テレビに寄る前の位置・向き)
    private Vector3 returnPosition;
    private Quaternion returnRotation;

    // テレビに寄っているか(戻り終わったら false)
    private bool zoomed;

    private Coroutine returnRoutine;


    /// <summary>明るくなってから目標時間を出すまでの秒数(サーバーが読む)</summary>
    public float TargetDelaySeconds => screen != null ? targetDelaySeconds : 0f;

    /// <summary>目標時間を出してから、カメラが元の位置に戻りきるまでの秒数(サーバーが読む)</summary>
    public float SecondsAfterTarget => screen != null ? holdSeconds + returnSeconds : 0f;


    // イベントは OnEnable で登録して、OnDisable で外す(MinigameInfo の決まり)
    private void OnEnable()
    {
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
        MinigameInfo.OnCustomEvent += HandleCustomEvent;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
        MinigameInfo.OnCustomEvent -= HandleCustomEvent;
    }

    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        switch (phase)
        {
            // 画面が暗い間に、テレビに寄った位置へ移っておく
            case MinigamePhase.RoundReset:
                ZoomToScreen();
                break;

            // 計測が始まるまでに戻りきっていなければ、操作に影響しないよう元の位置にそろえる
            case MinigamePhase.Countdown:
            case MinigamePhase.Playing:
                if (zoomed) { ReturnImmediately(); }
                break;
        }
    }

    // 目標時間が出たら、少し見せてから元の位置へ戻る
    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
    {
        if (eventName != StopwatchMinigame.TargetEventName) { return; }
        if (!zoomed) { return; } // 1ラウンド目(イントロ演出が映している)は何もしない

        if (returnRoutine != null) { StopCoroutine(returnRoutine); }
        returnRoutine = StartCoroutine(ReturnRoutine());
    }


    // ==================== ここから下は内部の処理 ====================

    // 今の位置を戻る先として覚えて、テレビの正面(今と同じ向きのまま、zoomDistance だけ手前)へ一瞬で移る
    private void ZoomToScreen()
    {
        if (screen == null) { return; }

        if (returnRoutine != null) { StopCoroutine(returnRoutine); returnRoutine = null; }
        if (!zoomed)
        {
            returnPosition = transform.position;
            returnRotation = transform.rotation;
        }

        zoomed = true;
        transform.SetPositionAndRotation(screen.position - (returnRotation * Vector3.forward) * zoomDistance, returnRotation);
    }

    private IEnumerator ReturnRoutine()
    {
        yield return new WaitForSeconds(holdSeconds);

        Vector3 from = transform.position;
        float t = 0f;
        while (t < returnSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / returnSeconds);
            float ease = k * k * (3f - 2f * k); // ゆっくり動き出して、ゆっくり止まる
            transform.position = Vector3.Lerp(from, returnPosition, ease);
            yield return null;
        }

        ReturnImmediately();
    }

    private void ReturnImmediately()
    {
        if (returnRoutine != null) { StopCoroutine(returnRoutine); returnRoutine = null; }
        transform.SetPositionAndRotation(returnPosition, returnRotation);
        zoomed = false;
    }
}
