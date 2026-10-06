using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ミニゲーム用プレイヤー入力
/// </summary>

// プレイヤーオブジェクトに,playerMovementと一緒にアタッチする。
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerMinigameInput : MonoBehaviour
{
    private PlayerInput playerInput;    // 入力管理
    private PlayerMovement playerMovement; // 移動ロックの切り替えに使う
    private InputAction buttonSouthAction; // ボタン入力のアクション

    // Aボタンが押されたときに発行されるイベント
    public event Action OnConfirmPressed;


    // 参照の取得をまとめて行う
    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        playerMovement = GetComponent<PlayerMovement>();
        buttonSouthAction = playerInput.actions["ButtonSouth"];
    }

    // イベントの購読を開始する
    private void OnEnable()
    {
        buttonSouthAction.performed += HandleButtonSouthPerformed;

        if(MinigameManager.Instance != null)
        {
            MinigameManager.Instance.OnMinigameChanged += HandleMinigameChanged;
        }
    }

    // イベントの購読を解除する
    private void OnDisable()
    {
        buttonSouthAction.performed -= HandleButtonSouthPerformed;

        if(MinigameManager.Instance != null)
        {
            MinigameManager.Instance.OnMinigameChanged -= HandleMinigameChanged;
        }
    }

    // Aボタンが押されたときの処理
    private void HandleButtonSouthPerformed(InputAction.CallbackContext context)
    {
        OnConfirmPressed?.Invoke();
    }

    // ミニゲームが切り替わったときの処理
    private void HandleMinigameChanged(MinigameType type)
    {
        switch(type)
        {
            case MinigameType.TimerStop:
                //移動禁止。Aボタンのみ操作可能にする
                playerMovement.SetMovementLocked(true);
                break;

            case MinigameType.None:
                //移動可能にする
                playerMovement.SetMovementLocked(false);
                break;
        }
    }
}
