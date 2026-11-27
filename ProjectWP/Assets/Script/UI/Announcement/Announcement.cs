using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 全員に知らせたいことを、画面の下に数秒だけ出すアナウンス(「2Pが抜けました」など)。
/// どのスクリプトからでも Announcement.Show("{文字|もじ}") で出せる。ルビの書き方は RubyText と同じ。
///
/// 表示は Resources/AnnouncementUI.prefab。初めて使ったときに作られ、シーンをまたいでも残る。
/// 続けて出したときは、前のアナウンスが消えてから順番に出す。
/// 出す位置を変えたいシーンには、AnnouncementPosition を付けたオブジェクトを置く。
/// この端末の画面にだけ出すので、全員に出したいときは RPC で全員の端末から呼ぶ
/// </summary>
public class Announcement : MonoBehaviour
{
    private const string PrefabPath = "AnnouncementUI";

    // 位置を指定していないシーンでの、帯の中心の位置(画面の割合。0,0 = 左下 / 1,1 = 右上)
    private static readonly Vector2 DefaultViewportPosition = new Vector2(0.5f, 0.07f);

    [Tooltip("帯全体(背景＋文字)。この位置を動かして、出す場所を変える")]
    [SerializeField] private RectTransform banner;

    [Tooltip("アナウンスの文字(AutoRubyTMP を付けるとルビが付く)")]
    [SerializeField] private TMP_Text messageText;

    [Tooltip("出し入れで透明度を変える。帯に付ける")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("1つのアナウンスを出しておく秒数(出し入れの時間は含まない)")]
    [SerializeField, Min(0.1f)] private float showSeconds = 3f;

    [Tooltip("ふわっと出す・消す秒数")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.25f;

    private static Announcement instance;

    // まだ出していないアナウンス(順番に出す)
    private readonly Queue<string> waiting = new Queue<string>();

    // 今出しているアナウンスの経過時間。-1 = 何も出していない
    private float elapsed = -1f;

    /// <summary>画面の下にアナウンスを数秒出す(この端末だけ)。例: Announcement.Show("2Pが{抜|ぬ}けました")</summary>
    public static void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) { return; }

        Announcement announcement = GetOrCreate();
        if (announcement == null) { return; }

        Debug.Log($"[Announcement] {RubyText.Strip(message)}");
        announcement.waiting.Enqueue(message);
    }

    private static Announcement GetOrCreate()
    {
        if (instance != null) { return instance; }

        Announcement prefab = Resources.Load<Announcement>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Announcement] Resources/{PrefabPath}.prefab が見つかりません");
            return null;
        }
        instance = Instantiate(prefab);
        instance.name = prefab.name;
        DontDestroyOnLoad(instance.gameObject);
        return instance;
    }

    private void Awake()
    {
        canvasGroup.alpha = 0f;
        banner.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) { instance = null; }
    }

    // フェードで画面が止まっていても動くように、unscaledDeltaTime で数える
    private void Update()
    {
        if (elapsed < 0f)
        {
            if (waiting.Count == 0) { return; }

            // 画面が暗い間(ローディング中・ラウンドの間の暗転中)は出さずに待ち、明るくなってから出す
            // (暗い間に出すと、見えないまま消えてしまうため)
            if (IsScreenCovered()) { return; }

            Begin(waiting.Dequeue());
        }

        elapsed += Time.unscaledDeltaTime;
        float total = fadeSeconds + showSeconds + fadeSeconds;

        if (elapsed >= total)
        {
            // 消し終わった。次があれば次の Update で出す
            elapsed = -1f;
            canvasGroup.alpha = 0f;
            banner.gameObject.SetActive(false);
            return;
        }

        // 出す → そのまま → 消す
        if (fadeSeconds <= 0f) { canvasGroup.alpha = 1f; }
        else if (elapsed < fadeSeconds) { canvasGroup.alpha = elapsed / fadeSeconds; }
        else if (elapsed > fadeSeconds + showSeconds) { canvasGroup.alpha = (total - elapsed) / fadeSeconds; }
        else { canvasGroup.alpha = 1f; }
    }

    // 画面が暗くなっているか(シーン移動のフェード・ローディング中)
    private static bool IsScreenCovered()
    {
        SceneLoader loader = SceneLoader.Instance;
        return loader != null && loader.IsScreenCovered;
    }

    // 1つのアナウンスを出し始める。位置はそのときのシーンの AnnouncementPosition に合わせる
    private void Begin(string message)
    {
        AnnouncementPosition position = FindFirstObjectByType<AnnouncementPosition>();
        Vector2 viewport = position != null ? position.ViewportPosition : DefaultViewportPosition;
        banner.anchorMin = viewport;
        banner.anchorMax = viewport;
        banner.anchoredPosition = Vector2.zero;

        messageText.SetRubyText(message);
        banner.gameObject.SetActive(true);
        canvasGroup.alpha = 0f;
        elapsed = 0f;
    }
}
