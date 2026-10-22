using TMPro;
using UnityEngine;

/// <summary>
/// オンラインのシーン遷移で、自分の読み込みが終わって他の人を待っている間だけ、
/// ローディング表示の上に小さなお知らせ(「他の人の読み込みを待っています」など)を出すクラス。
///
/// Resources/SceneLoader.prefab の中に、ローディング表示(LordingUI)とは別のCanvasとして置いてある。
/// (ローディング表示はチームメンバーの担当なので、中身には手を入れず、手前に重ねて表示する)
/// SceneLoaderと同じオブジェクトに付いているので、SceneLoader.Instance から GetComponent で取れる。
/// いつ出していつ消すかは NetworkSceneTransition が決める。
/// </summary>
public class LoadingWaitNotice : MonoBehaviour
{
    [Tooltip("お知らせ全体(文字を入れているCanvas)。表示/非表示はこれごと切り替える")]
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
