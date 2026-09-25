using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーを動かすためのスクリプト
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerMovement : MonoBehaviour
{
    [Header("移動速度")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("回転速度")]
    [SerializeField] private float rotationSpeed = 10f;

    [Header("重力")]
    [SerializeField] private float gravity = -9.81f;

    private CharacterController characterController; // 移動、当たり判定を担当するコンポーネント
    private PlayerInput playerInput; // 入力を管理するコンポーネント
    private InputAction moveAction; // 移動入力を取得するためのアクション
    private Animator animator; // アニメーションを再生するためのコンポーネント
    private Transform mainCameraTransform; // メインカメラのTransform

    private Vector2 moveInput; // 入力された移動方向
    private float verticalVelocity; // 重力によって落ちていく速さ

    private static readonly int RunningHash = Animator.StringToHash("IsRunning"); // アニメーションのSpeedパラメータのハッシュ値


    private void Awake()
    {
        // コンポーネントの取得
        characterController = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        animator = GetComponentInChildren<Animator>();

        if(Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
    }

    // 移動入力アクションはAwakeではなくOnEnableで取得する。
    // オンラインでは、PlayerInputが有効化された後(=自分専用の入力設定が
    // 作られた後)に、このコンポーネントが有効化されるため。
    // Awakeで取ると、有効化前の共有アクションを掴んでしまい入力が読めない。
    private void OnEnable()
    {
        moveAction = playerInput.actions["Move"];

        // オンラインではキャラクターが別のシーンで作られ、その後シーンをまたいで使われるため、
        // Awakeで掴んだカメラは既に無くなっている。有効になるたびに取り直す。
        if(Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
       ReadInput();

       Vector3 moveDirection = GetMoveDirection();

       RotateCharacter(moveDirection);
       ApplyGravity();
       Move(moveDirection);
       UpdateAnimator(moveDirection);
    }

    // 現在の入力を取得する
    private void ReadInput()
    {
        //方向ベクトルを取得
        moveInput = moveAction.ReadValue<Vector2>();
    }

    // 入力に応じてキャラクターを回転させる
    private void RotateCharacter(Vector3 direction)
    {
        if(direction.sqrMagnitude < 0.01f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        // 毎フレーム少しずつ回転させることで滑らかな回転を実現する
        transform.rotation = Quaternion.Slerp(
            transform.rotation, 
            targetRotation, 
            rotationSpeed * Time.deltaTime
        );
    }


    private void ApplyGravity()
    {
        if(characterController.isGrounded && verticalVelocity < 0f)
        {
            // 地面にいるときは少し下方向に力を加えて地面に張り付かせる
            verticalVelocity = -2f; 
        }

        verticalVelocity += gravity * Time.deltaTime;
    }

    // キャラクターを移動する処理
    private void Move(Vector3 direction)
    {
        // 斜め移動の速度を一定にするために必要
        Vector3 verocity = direction.normalized * moveSpeed;

        // 重力の影響を加える
        verocity.y = verticalVelocity;

        // 当たり判定を自動で計算して移動する
        characterController.Move(verocity * Time.deltaTime);
    }


    // 移動量に応じてアニメーションを切り替える処理
    private void UpdateAnimator(Vector3 direction)
    {
        if(animator == null)
        {
            return;
        }

        animator.SetBool(RunningHash, direction.sqrMagnitude > 0f);
    }

    private Vector3 GetMoveDirection()
    {
        // カメラが見つからない場合は、従来通りワールド基準で計算する(保険)
        if (mainCameraTransform == null)
        {
            return new Vector3(moveInput.x, 0f, moveInput.y);
        }

        // カメラの前方向・右方向を取得し、上下方向(Y)を無視して水平面だけにする
        Vector3 cameraForward = mainCameraTransform.forward;
        Vector3 cameraRight = mainCameraTransform.right;
        cameraForward.y = 0f;
        cameraRight.y = 0f;
        cameraForward.Normalize();
        cameraRight.Normalize();

        return cameraForward * moveInput.y + cameraRight * moveInput.x;
    }
}