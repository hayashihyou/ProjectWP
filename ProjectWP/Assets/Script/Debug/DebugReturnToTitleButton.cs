using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 【テスト用】「タイトルに戻る」を試すためのボタン。
///
/// ネットワークに繋がっていて、タイトル以外のシーンにいる間だけ、画面右上にボタンを出す。
///   ホストが押す   → 部屋を解散してタイトルへ(参加者も自動でタイトルに戻る)
///   参加者が押す   → 自分だけ抜けてタイトルへ
///
/// 簡単に消せるように、シーンには何も置かない作りにしている。
///   - ゲーム開始時に自動で生成される(RuntimeInitializeOnLoadMethod)
///   - 見た目はOnGUIで描くので、Canvas/ボタンのprefab/TextMeshProも不要
/// 本番用の「終了後の選択」画面ができたら、このファイル(と.meta)を消すだけでよい。
/// </summary>
public class DebugReturnToTitleButton : MonoBehaviour
{
    private const float ButtonWidth = 260f;
    private const float ButtonHeight = 56f;
    private const float Margin = 16f;

    // 押した後にもう一度押せないようにする(切断が終わると自動で戻る)
    private bool pressed;

    private GUIStyle buttonStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStartup()
    {
        var go = new GameObject("[Debug] ReturnToTitleButton");
        DontDestroyOnLoad(go);
        go.AddComponent<DebugReturnToTitleButton>();
    }

    private void OnGUI()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            pressed = false;
            return;
        }

        if (SceneManager.GetActiveScene().name == SceneNames.Title) return;
        if (NetworkSceneTransition.Instance == null) return;

        // GUIのスタイルはOnGUIの中でしか作れないので、最初の1回だけここで作る
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 20 };
        }

        string label = networkManager.IsHost ? "解散してタイトルへ(テスト)" : "抜けてタイトルへ(テスト)";
        var rect = new Rect(Screen.width - ButtonWidth - Margin, Margin, ButtonWidth, ButtonHeight);

        GUI.enabled = !pressed;
        if (GUI.Button(rect, label, buttonStyle))
        {
            pressed = true;
            NetworkSceneTransition.Instance.ReturnToTitleAsync().Forget();
        }
        GUI.enabled = true;
    }
}
