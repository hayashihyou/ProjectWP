using TMPro;
using UnityEngine;

/// <summary>
/// 画面の中央に、全体へのお知らせ(「ホストが部屋を解散しました」など)を出すクラス。
///
/// Resources/SceneLoader.prefab の FadeCanvas の中に置いてある。
/// フェード用の黒画像(Image)より後ろ(=手前に描かれる)に置き、黒画像のCanvasGroupの外にしてあるので、
/// お知らせを出したまま画面を暗くしても、文字は見えたまま残る。
/// SceneLoaderと同じオブジェクトに付いているので、SceneLoader.Instance から GetComponent で取れる。
/// </summary>
public class ScreenNotice : MonoBehaviour
{
    [Tooltip("お知らせ全体(背景の帯＋文字)。表示/非表示はこれごと切り替える")]
    [SerializeField] private GameObject noticeRoot;

    [Tooltip("お知らせの文字")]
    [SerializeField] private TMP_Text noticeText;

    private void Awake()
    {
        Hide();
    }

    public void Show(string message)
    {
        if (noticeText != null) noticeText.text = message;
        if (noticeRoot != null) noticeRoot.SetActive(true);
    }

    public void Hide()
    {
        if (noticeRoot != null) noticeRoot.SetActive(false);
    }
}
