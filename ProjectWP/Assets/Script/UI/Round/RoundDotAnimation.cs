using System.Collections;
using UnityEngine;

/// <summary>
/// ラウンド表示の「点」1つ分のアニメーション。roundDot(点の元オブジェクト)に付ける。
///
/// - 現在のラウンドの点は大きく、それ以外は小さく表示する
/// - 現在のラウンドの点は「大 → 小 → 大」と脈打ち、指定回数で止まる
///
/// 点は RoundAnimationController が元オブジェクトから複製するので、
/// ここで設定した大きさや時間が、複製された全部の点に引き継がれる。
/// </summary>
public class RoundDotAnimation : MonoBehaviour
{
    [Header("スケール設定")]
    [Tooltip("現在のラウンドの点のスケール(大きい点)")]
    [SerializeField] private float activeScale = 1.4f;

    [Tooltip("それ以外の点のスケール(小さい点)")]
    [SerializeField] private float inactiveScale = 0.7f;

    [Header("脈動アニメーション(秒)")]
    [Tooltip("脈動を行う")]
    [SerializeField] private bool pulseEnabled = true;

    [Tooltip("再生開始から何秒後に脈動を始めるか")]
    [SerializeField] private float pulseStartDelay = 0.6f;

    [Tooltip("脈動の回数(大→小→大 で1回。この回数が終わると止まる)")]
    [SerializeField] private int pulseCount = 2;

    [Tooltip("脈動時に縮むときの大きさ")]
    [SerializeField] private float pulseSmallScale = 0.7f;

    [Tooltip("大 → 小 にかける時間")]
    [SerializeField] private float shrinkDuration = 0.25f;

    [Tooltip("小さい状態で止まる時間")]
    [SerializeField] private float smallHoldDuration = 0.1f;

    [Tooltip("小 → 大 にかける時間")]
    [SerializeField] private float growDuration = 0.25f;

    [Tooltip("次の脈動までの待ち時間(大きい状態で止まる時間)")]
    [SerializeField] private float pulseInterval = 0.2f;

    [Tooltip("脈動のカーブ(0〜1)")]
    [SerializeField] private AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("オプション")]
    [Tooltip("Time.timeScale の影響を受けない(ポーズ中でも動かしたい場合)")]
    [SerializeField] private bool useUnscaledTime = false;

    private Coroutine pulseRoutine;
    private bool isCurrent;

    /// <summary>この点が現在のラウンドかどうかを設定し、大きさを即座に反映する。</summary>
    public void SetCurrent(bool current)
    {
        StopPulse();
        isCurrent = current;
        transform.localScale = Vector3.one * (isCurrent ? activeScale : inactiveScale);
    }

    /// <summary>脈動を最初から再生する(現在のラウンドの点だけ動く)。</summary>
    public void PlayPulse()
    {
        StopPulse();
        if (!isCurrent || !pulseEnabled || !gameObject.activeInHierarchy) return;
        pulseRoutine = StartCoroutine(PulseRoutine());
    }

    /// <summary>脈動を止める。</summary>
    public void StopPulse()
    {
        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
        }
    }

    private void OnDisable()
    {
        pulseRoutine = null;
    }

    private IEnumerator PulseRoutine()
    {
        yield return Wait(pulseStartDelay);

        for (int i = 0; i < pulseCount; i++)
        {
            // 大 → 小
            yield return Animate(shrinkDuration, t =>
            {
                transform.localScale = Vector3.one * Mathf.Lerp(activeScale, pulseSmallScale, t);
            });

            // 小さいまま少し止まる
            yield return Wait(smallHoldDuration);

            // 小 → 大
            yield return Animate(growDuration, t =>
            {
                transform.localScale = Vector3.one * Mathf.Lerp(pulseSmallScale, activeScale, t);
            });

            // 次の脈動までの待ち(最後の1回の後は待たない)
            if (i < pulseCount - 1)
            {
                yield return Wait(pulseInterval);
            }
        }

        // 最後は必ず大きい状態で止める
        transform.localScale = Vector3.one * activeScale;
        pulseRoutine = null;
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
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            onUpdate(pulseCurve.Evaluate(t));
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

#if UNITY_EDITOR
    private void OnValidate()
    {
        pulseCount = Mathf.Max(0, pulseCount);
    }
#endif
}