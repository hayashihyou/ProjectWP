using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 待機画面(02_PlayerJoin)右下の「ゲームスタート」ボタン。
///
/// 押せるのはホストだけ(ゲームを始めるタイミングはホストが決める、という設計)。
/// 押すと、サーバー側からシーンを切り替える。NetworkConfig.EnableSceneManagementが
/// 有効なので、この後に参加してくるクライアントも自動で同じシーンに揃う。
///
/// ホスト以外にもこのボタン自体は表示したままにする。何も表示しないと
/// 「どこから始めればいいか分からない」状態になってしまうため、非ホストには
/// 見た目をグレーアウトさせ(Buttonのinteractable=falseによる標準の無効化表示)、
/// ラベルを「ホストの開始を待っています...」に変えて、今はホストの操作待ちだと
/// 分かるようにする。
/// </summary>
public class GameStartButton : MonoBehaviour
{
    [SerializeField] private Button startButton;

    // 遷移先のシーン名。移動の動作確認用に、一時的にTest_Playerへ飛ばしている。
    // 本来は "03_StageSelect"(まだ中身が無いプレースホルダーのシーン)に戻す。
    private const string NextSceneName = "Test_Player";

    private const string HostLabelText = "ゲームスタート";
    private const string WaitingForHostLabelText = "ホストの開始を待っています...";

    private void Start()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        if (startButton != null)
        {
            // ホスト以外は押せないようにする。Buttonのinteractable=falseは
            // 見た目も自動でグレーアウトしてくれる(Disabled Colorの標準機能)ので、
            // 「今は操作できない」ことが視覚的にも伝わる。
            startButton.interactable = isHost;
            if (isHost)
            {
                startButton.onClick.AddListener(OnStartButtonClicked);
            }
        }

        TMP_Text label = GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = isHost ? HostLabelText : WaitingForHostLabelText;
        }

        // ホストの場合、ゲームパッド/キーボードのAボタン(決定)ですぐ押せるように
        // 最初から選択状態にしておく。
        if (isHost && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }

    private void OnStartButtonClicked()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(NextSceneName, LoadSceneMode.Single);
    }
}
