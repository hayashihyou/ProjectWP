using TMPro;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// ミニゲーム選択ボタン
/// </summary>
[RequireComponent(typeof(Button))]
public class MinigameSelectButton : MonoBehaviour
{
    [Header("シーン内のMinigameSelectManager")]
    [SerializeField] private MinigameSelectManager minigameSelectManager;

    [Header("このボタンで選ばれるミニゲームの種類")]
    [SerializeField] private MinigameType minigameType;

    [Header("遷移先のシーン名(Build Settingsに登録されている名前と完全一致させる)")]
    [SerializeField] private string sceneName;

    [Header("遊べるミニゲームかどうか。オフにすると押せなくなる")]
    [SerializeField] private bool isAvailable = true;

    [Header("●を選んだときに出すミニゲームの名前")]
    [SerializeField] private string displayName;

    [Header("名前を表示するテキスト(このプレハブの子の吹き出しの中)")]
    [SerializeField] private TMP_Text nameText;


    private Button button; // ボタンコンポーネント


    // ボタンの状態設定と、クリック時の処理の登録を行う
    private void Awake()
    {
        button = GetComponent<Button>();
        button.interactable = isAvailable;
        button.onClick.AddListener(OnClicked);

        // ●の上の吹き出しに名前を入れる(遊べないものは「準備中」をつける)
        if (nameText != null) { nameText.text = isAvailable ? displayName : displayName + "\n(準備中)"; }
    }

    private void OnDestroy()
    {
        button.onClick.RemoveListener(OnClicked);
    }


    // クリック・タップ・Aボタンで決定したとき
    private void OnClicked()
    {
        if (!isAvailable)
        {
            return;
        }

        minigameSelectManager.StartMinigame(minigameType, sceneName);
    }
}