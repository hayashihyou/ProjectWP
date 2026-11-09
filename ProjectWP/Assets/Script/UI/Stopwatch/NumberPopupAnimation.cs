using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class NumberPopupAnimation : MonoBehaviour
{
    [Header("対象")]
    [SerializeField] private TMP_Text numberText;
    [SerializeField] private float displayNumber = 0.20f;  // int → float に変更
    [SerializeField] private string suffix = "";           // 末尾に付ける文字(例: "秒" や "s"。不要なら空)

    [Header("タイミング")]
    [SerializeField] private float delayBeforeMove = 0.5f;

    [Header("移動")]
    [SerializeField] private float moveDistance = 100f;
    [SerializeField] private float moveDuration = 1.0f;
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("フェード")]
    [Range(0f, 1f)][SerializeField] private float startAlpha = 1f;
    [Range(0f, 1f)][SerializeField] private float endAlpha = 0f;
    [SerializeField] private float fadeDuration = 1.0f;

    [Header("デバッグ")]
    [SerializeField] private bool useDebugKey = true;
    [SerializeField] private Key debugKey = Key.Space; // プルダウンでキーを選択

    private Vector3 startLocalPos;
    private Coroutine playing;

    private void Reset()
    {
        numberText = GetComponent<TMP_Text>();
    }

    private void Awake()
    {
        if (numberText == null) numberText = GetComponent<TMP_Text>();
        startLocalPos = numberText.transform.localPosition;
        SetAlpha(0f);
    }

    private void Update()
    {
        if (!useDebugKey) return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[debugKey].wasPressedThisFrame)
        {
            Play();
        }
    }

    public void Play()
    {
        Play(displayNumber);
    }

    public void Play(float number)
    {
        if (playing != null) StopCoroutine(playing);
        numberText.text = FormatNumber(number);
        playing = StartCoroutine(AnimationRoutine());
    }

    private string FormatNumber(float number)
    {
        // 符号は無視して、常に +0.00 の形式で表示する
        return "+" + Mathf.Abs(number).ToString("0.00") + suffix;
    }

    private IEnumerator AnimationRoutine()
    {
        Transform t = numberText.transform;
        t.localPosition = startLocalPos;
        SetAlpha(startAlpha);

        yield return new WaitForSeconds(delayBeforeMove);

        Vector3 endPos = startLocalPos + Vector3.up * moveDistance;
        float duration = Mathf.Max(moveDuration, fadeDuration);
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;

            float moveT = moveDuration > 0f ? Mathf.Clamp01(time / moveDuration) : 1f;
            t.localPosition = Vector3.LerpUnclamped(startLocalPos, endPos, moveCurve.Evaluate(moveT));

            float fadeT = fadeDuration > 0f ? Mathf.Clamp01(time / fadeDuration) : 1f;
            SetAlpha(Mathf.Lerp(startAlpha, endAlpha, fadeT));

            yield return null;
        }

        t.localPosition = endPos;
        SetAlpha(endAlpha);
        playing = null;
    }

    private void SetAlpha(float a)
    {
        Color c = numberText.color;
        c.a = a;
        numberText.color = c;
    }
}