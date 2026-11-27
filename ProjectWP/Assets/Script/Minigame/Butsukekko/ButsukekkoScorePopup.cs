using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 点数が増えた人の頭の上に「+1」を出す。
///   1. ポン : 大きく出て、すぐ元の大きさに縮む(インパクトの瞬間を分かりやすく)
///   2. 止まる : その場でくっきり見せる
///   3. 消える : 上に動きながら透明になる
/// MG_Butsukekko の ScoreCanvas > ScorePopup に付ける。
///
/// 点数が変わると、全員の端末で MinigameInfo.OnPlayerValueChanged が呼ばれるので、
/// 増えたとき(= 当てたとき)だけ、その人の頭の上に出す。全員の画面に出る。
///
/// 見た目は「お手本」の文字(template)を複製して作る。フォントや大きさを変えたいときは、お手本を直せばよい。
/// 文字の色は、その人の色(ButsukekkoHitManager のエフェクトと同じ色)にする。
/// 2人が続けて当てても、それぞれ別の文字を出すので、途中で消えたりしない。
/// </summary>
public class ButsukekkoScorePopup : MonoBehaviour
{
    [Tooltip("お手本の文字(非表示にしておく)。出すたびにこれを複製する")]
    [SerializeField] private TMP_Text template;

    [Tooltip("色を教えてもらう係(Minigame オブジェクトの ButsukekkoHitManager)")]
    [SerializeField] private ButsukekkoHitManager hitManager;

    [Header("出す位置")]
    [Tooltip("足元からこの高さ(m)に出す。キャラの頭の少し上")]
    [SerializeField] private float headHeight = 1.8f;

    [Header("1. ポン(大きく出て縮む)")]
    [Tooltip("出た瞬間の大きさ(倍)")]
    [SerializeField] private float popScale = 1.5f;

    [Tooltip("元の大きさに縮むまでの秒数")]
    [SerializeField] private float popSeconds = 0.1f;

    [Header("2. 止まる")]
    [Tooltip("出てから、消え始めるまでの秒数(この間はくっきり見える)")]
    [SerializeField] private float holdSeconds = 0.25f;

    [Header("3. 消える")]
    [Tooltip("上に動きながら消えるまでの秒数")]
    [SerializeField] private float fadeSeconds = 0.35f;

    [Tooltip("消えるまでに上に動く距離(画面のピクセル)")]
    [SerializeField] private float moveDistance = 100f;

    [Tooltip("動き方(横 = 時間 0〜1、縦 = 動いた割合 0〜1)。最初に速く動いて、ゆっくり止まる")]
    [SerializeField] private AnimationCurve moveCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));

    // 文字を並べる場所(お手本の親)。画面いっぱいの大きさにしておく
    private RectTransform area;

    private void Awake()
    {
        area = template != null ? (RectTransform)template.transform.parent : (RectTransform)transform;
        if (template != null) { template.gameObject.SetActive(false); }
    }

    private void OnEnable()
    {
        MinigameInfo.OnPlayerValueChanged += HandlePlayerValueChanged;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPlayerValueChanged -= HandlePlayerValueChanged;
    }

    // 誰かの点数が増えた → その人の頭の上に「+増えた分」を出す
    private void HandlePlayerValueChanged(int slot, float delta, float newValue)
    {
        if (delta <= 0f) { return; }

        PlayerSlot player = FindPlayer(slot);
        if (player == null || template == null) { return; }

        StartCoroutine(PopupRoutine(player.transform, slot, Mathf.RoundToInt(delta)));
    }

    private IEnumerator PopupRoutine(Transform player, int slot, int amount)
    {
        // お手本を複製して、文字と色を決める
        TMP_Text text = Instantiate(template, template.transform.parent);
        text.gameObject.SetActive(true);
        text.text = "+" + amount;
        Color color = hitManager != null ? hitManager.GetPlayerColor(slot) : Color.white;

        RectTransform rect = text.rectTransform;
        float total = holdSeconds + fadeSeconds;
        float time = 0f;
        while (time < total && player != null)
        {
            // 1. ポン : 最初の popSeconds で popScale 倍 → 1倍
            float popT = popSeconds > 0f ? Mathf.Clamp01(time / popSeconds) : 1f;
            rect.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, 1f - (1f - popT) * (1f - popT));

            // 2. 止まる → 3. 消える : 止まっている間は 0、消え始めてから 0〜1 に進む
            float fadeT = fadeSeconds > 0f ? Mathf.Clamp01((time - holdSeconds) / fadeSeconds) : 1f;

            // 頭の位置を画面上の位置に直して、そこから上にずらす(キャラが動いてもついていく)
            Vector2 headOnCanvas;
            if (TryGetCanvasPosition(player.position + Vector3.up * headHeight, out headOnCanvas))
            {
                rect.anchoredPosition = headOnCanvas + Vector2.up * moveDistance * moveCurve.Evaluate(fadeT);
            }

            // だんだん透明にする(止まっている間は 1 のまま)
            color.a = 1f - fadeT;
            text.color = color;

            yield return null;
            time += Time.deltaTime;
        }

        Destroy(text.gameObject);
    }

    // 3Dの位置を、文字を並べる場所の中の位置(真ん中が 0,0)に直す。カメラの後ろなら false
    private bool TryGetCanvasPosition(Vector3 worldPosition, out Vector2 canvasPosition)
    {
        canvasPosition = Vector2.zero;
        Camera cam = Camera.main;
        if (cam == null) { return false; }

        Vector3 screen = cam.WorldToScreenPoint(worldPosition);
        if (screen.z < 0f) { return false; }

        // 画面に直接描くキャンバス(Overlay)なので、カメラは null を渡す
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out canvasPosition);
    }

    private static PlayerSlot FindPlayer(int slot)
    {
        foreach (PlayerSlot player in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            if (player.SlotIndex == slot) { return player; }
        }
        return null;
    }
}
