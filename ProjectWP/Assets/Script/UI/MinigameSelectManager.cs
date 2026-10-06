using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;


/// <summary>
/// ミニゲームセレクト管理
/// </summary>
public class MinigameSelectManager : MonoBehaviour
{
    [Header("コントローラー、キーボード操作で、最初に選択状態にするボタン")]
    [SerializeField] private Button firstSelectedButton;


    // 画面表示時に最初のボタンを選択状態にする
    private void Start()
    {
        SelectFirstButton();
    }


    // 更新処理
    private void Update()
    {
        RestoreSelectionOnNavigate();
    }


    // 最初のボタンを選択状態にする
    private void SelectFirstButton()
    {
        if(firstSelectedButton == null || EventSystem.current ==null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(firstSelectedButton.gameObject);
    }


    // マウスやタップで何もない場所を押すと選択状態が解除される為
    // その状態で十字キーやスティックを操作したら、最初のボタンを選択状態に戻す
    private void RestoreSelectionOnNavigate()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || eventSystem.currentSelectedGameObject != null)
        {
            return;
        }

        InputSystemUIInputModule module = eventSystem.currentInputModule as InputSystemUIInputModule;

        if(module == null || module == null || module.move == null)
        {
            return;
        }

        if(module.move.action.WasPerformedThisFrame())
        {
            SelectFirstButton();
        }
    }


    // 選ばれたミニゲームを保存し、指定のシーンへフェード付きで遷移する
    public void StartMinigame(MinigameType type, string sceneName)
    {
        if(MinigameManager.Instance != null)
        {
            MinigameManager.Instance.SetSelectedMinigame(type);
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        bool isOnline = networkManager != null && networkManager.IsListening;

        if (isOnline && NetworkSceneTransition.Instance != null)
        {
            // 接続後：ホストが全員をフェード付きで移動させる(参加者が押しても何も起きない)
            NetworkSceneTransition.Instance.LoadSceneForAllAsync(sceneName).Forget();
        }

        else
        {
            // 接続前：このシーンを単体で再生した時など
            SceneLoader.Instance.LoadSceneAsync(sceneName, useFade: true).Forget();
        }
    }

}
