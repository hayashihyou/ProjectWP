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
    // 入力アクション名(PlayerControls.inputActions の Player マップに同じ名前で追加する)
    private const string ConfirmActionName = "Confirm";

    // ミニゲーム側(StopwatchMinigame.cs)の StopActionName と同じにする(1文字でも違うと届かない)
    private const string StopwatchAction = "Stop";

    private PlayerInput playerInput; // 入力管理
    private PlayerMovement playerMovement; // 移動ロックの切り替えを行う

    private MinigameType currentType = MinigameType.None; // 現在のミニゲームタイプ

    // アクションが見つからない警告を毎フレーム出さないためのフラグ
    private bool warnedMissingAction;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        // ミニゲームが切り替わったら移動ロックなどのルールを切り替える
        MinigameType type = GetCurrentMinigameType();
        if (type != currentType)
        {
            currentType = type;
            ApplyControlRules(type);
        }

        //自身のキャラクター以外(PlayerOwnerControl が PlayerInput を無効にしている)
        if(!playerInput.enabled) return;

        if (type == MinigameType.None) return;

        if (!MinigameInfo.CanControl) return;

        HandleMinigameInput(type);
    }

    private void HandleMinigameInput(MinigameType type)
    {
        switch(type)
        {
            case MinigameType.Stopwatch:
               if(WasPressedThisFrame(ConfirmActionName))
                {
                    MinigameInfo.SendAction(StopwatchAction);
                }
                break;
        }
    }

    // ミニゲームごとの操作ルール(移動できるか等)
    private void ApplyControlRules(MinigameType type)
    {
        switch(type)
        {
            case MinigameType.Stopwatch:
                // 移動禁止。決定ボタンのみ操作可能にする
                playerMovement.SetMovementLocked(true);
                break;

            default:
                // ミニゲーム外、またはルール未設定のミニゲームは移動可能にする
                playerMovement.SetMovementLocked(false);
                break;
        }
    }

    // 指定したアクションがこのフレームで押されたか
    private bool WasPressedThisFrame(string actionName)
    {
       InputAction action = playerInput.actions != null ? playerInput.actions.FindAction(actionName) : null;
        if(action == null)
        {
            if(!warnedMissingAction)
            {
                Debug.LogWarning($"入力アクションが見つかりません。PlayerControls.inputactionsに追加してください");
                warnedMissingAction = true;
            }
            return false;
        }
        return action.WasPressedThisFrame();
    }

    // 現在のミニゲームタイプを取得する
    private MinigameType GetCurrentMinigameType()
    {
       return MinigameInfo.CurrentType;
    }
}