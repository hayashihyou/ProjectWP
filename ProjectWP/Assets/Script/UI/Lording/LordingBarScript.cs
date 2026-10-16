using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// ローディングバーを動かすだけのコンポーネント。
/// シーンのロードは行わない。SceneLoader から SetProgress で進捗(0〜1)を受け取る。
/// SceneLoader の Loading Indicator として設定したオブジェクト(またはその子)に付ける。
/// </summary>
public class LoadingBar : MonoBehaviour
{
    [SerializeField] private Slider slider;          // Min 0 / Max 1 / Whole Numbers オフ

    [Tooltip("バーが進む最大速度(1秒あたりの割合)。小さいほどゆっくり")]
    [SerializeField] private float fillSpeed = 1.5f;

    [Tooltip("完了時のフェードアウト用。Panel内の一番手前(一番下の子)に置く、全画面の黒いImage。任意")]
    [SerializeField] private Image fadeOverlay;

    [Tooltip("進捗を 0〜100% の数字で表示するTextMeshPro。任意(未設定なら数字は出ない)")]
    [SerializeField] private TMP_Text percentText;

    private float targetProgress;
    private int lastPercent = -1;

    // 割り当て忘れの保険: SliderはLoadingBar以下から自動で探す
    private void Awake()
    {
        if (slider == null) slider = GetComponentInChildren<Slider>(true);
    }

    // Loading Indicator が表示されるたびに 0 から始める
    private void OnEnable()
    {
        targetProgress = 0f;
        slider.value = 0f;
        SetOverlayAlpha(0f);   // 黒い覆いは透明に戻す
        lastPercent = -1;
        UpdatePercentText();
    }

    private void Update()
    {
        // 受け取った進捗に向かって滑らかに追従
        slider.value = Mathf.MoveTowards(slider.value, targetProgress, fillSpeed * Time.unscaledDeltaTime);
        UpdatePercentText();
    }

    // バーの見た目の値(slider.value)に合わせて、0〜100 の数字を更新する。
    // 数字が変わったときだけ文字を書き換える(毎フレーム文字列を作らないため)。
    private void UpdatePercentText()
    {
        if (percentText == null) return;

        int percent = Mathf.Clamp(Mathf.RoundToInt(slider.value * 100f), 0, 100);
        if (percent == lastPercent) return;

        lastPercent = percent;
        percentText.text = percent + "%";
    }

    /// <summary>バーが満タン(100%)まで表示されたか</summary>
    public bool IsFilled => slider.value >= 1f;

    /// <summary>進捗を 0〜1 で渡す(例: op.progress / 0.9f)</summary>
    public void SetProgress(float progress)
    {
        targetProgress = Mathf.Clamp01(progress);
    }

    /// <summary>
    /// ローディング表示全体を duration 秒かけて暗転させる(fadeOverlay未設定なら何もしない)。
    /// 個々のUIを透明にするのではなく、手前の黒い画像を濃くするので、
    /// 背景のバーが透けて見えることがない。
    /// </summary>
    public async UniTask FadeOutAsync(float duration, CancellationToken ct = default)
    {
        if (fadeOverlay == null)
        {
            Debug.LogWarning("[LoadingBar] Fade Overlay が未設定のため、フェードアウトをスキップしました");
            return;
        }
        if (duration <= 0f) return;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetOverlayAlpha(Mathf.Clamp01(elapsed / duration));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        SetOverlayAlpha(1f);
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (fadeOverlay == null) return;
        var c = fadeOverlay.color;
        c.a = alpha;
        fadeOverlay.color = c;
    }
}