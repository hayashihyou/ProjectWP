using System;
using UnityEngine;

/// <summary>
/// ミニゲーム管理
/// </summary>
public class MinigameManager : MonoBehaviour
{
    // どこからでもMinigameManager.Instanceでアクセスできるようにする
    public static MinigameManager Instance { get; private set; }

    // ミニゲームセレクトで選ばれたミニゲーム(シーンを跨いで保持される)
    public MinigameType SelectedMinigame { get; private set; } = MinigameType.None;

    // 現在選択中のミニゲーム(外部からは読み取り可能)
    public MinigameType CurrentMinigame { get; private set; } = MinigameType.None;

    // ミニゲームが変更されたときに呼ばれるイベント
    public event Action<MinigameType> OnMinigameChanged;


    private void Awake()
    {
        if(Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // シーンを跨いで保持する
        DontDestroyOnLoad(gameObject); 
    }


    // ミニゲームセレクトで選択されたミニゲームを保存する
    public void SetSelectedMinigame(MinigameType type)
    {
        SelectedMinigame = type;
    }


    // ミニゲームを切り替える。各ミニゲームはスクリプトはこれを呼んで開始を通知する
    public void SelectMinigame(MinigameType type)
    {
        CurrentMinigame = type;
        OnMinigameChanged?.Invoke(type);
    }
}
