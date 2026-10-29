using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 2つのテキストをクロスフェードして切り替える。
/// 旧テキストが消えながら、同時に新テキストが現れる。
/// </summary>
public class ReadyTextSwitcher : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TMP_Text beforeText;   // 「準備中」
    [SerializeField] private TMP_Text afterText;    // 「OK」

    [Header("切り替え設定")]
    [Tooltip("クロスフェードにかける秒数(0なら即切り替え)")]
    [SerializeField, Min(0f)] private float crossFadeDuration = 0.25f;

    [Tooltip("押してから切り替え開始までの遅延(秒)")]
    [SerializeField, Min(0f)] private float startDelay = 0f;

    private Coroutine running;

    private void Awake()
    {
        ResetState();
    }

    /// <summary>初期状態(準備中を表示、OKを非表示)に戻す</summary>
    public void ResetState()
    {
        if (running != null) StopCoroutine(running);
        SetAlpha(beforeText, 1f);
        SetAlpha(afterText, 0f);
    }

    /// <summary>切り替え開始</summary>
    public void Play()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(CrossFade());
    }

    private IEnumerator CrossFade()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        if (crossFadeDuration <= 0f)
        {
            SetAlpha(beforeText, 0f);
            SetAlpha(afterText, 1f);
            yield break;
        }

        float t = 0f;
        while (t < crossFadeDuration)
        {
            t += Time.deltaTime;
            float rate = Mathf.Clamp01(t / crossFadeDuration);
            SetAlpha(beforeText, 1f - rate);  // 消えながら
            SetAlpha(afterText, rate);        // 同時に現れる
            yield return null;
        }

        SetAlpha(beforeText, 0f);
        SetAlpha(afterText, 1f);
        running = null;
    }

    private static void SetAlpha(TMP_Text text, float alpha)
    {
        if (text == null) return;
        Color c = text.color;
        c.a = alpha;
        text.color = c;
    }
}