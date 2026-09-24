using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// 「このキャラクターを操作してよいのは誰か」を決めるクラス。
///
/// オンラインでは全員のPCに全員分のキャラクターが存在する。何も対策しないと、
/// 全員のPCで全キャラクターの入力処理が動いてしまい、1人が操作すると
/// 全員のキャラクターが動いてしまう。そこで、
///   - 自分のキャラクター(IsOwner) → 入力と移動を有効にする
///   - 他人のキャラクター           → 入力と移動を無効にする(位置はNetworkTransformで届く)
/// と切り分ける。位置の同期は「所有者主導」(NetworkTransformのAuthorityMode=Owner)で、
/// 操作した本人のPCで動かした結果をそのまま他の人に送る方式。
///
/// 無効にするもの(prefab上は最初から無効にしてあり、ここで必要な人だけ有効にする):
///   PlayerInput      : 有効になった瞬間にキーボード/ゲームパッドを掴むため、他人のキャラでは絶対に有効にしない
///   CharacterController / PlayerMovement : 他人のキャラを自分のPCで勝手に動かさないため
///
/// 移動を止めるシーン: 待機画面などでは、キャラクターは枠に立っているだけにしたいので、
/// movementDisabledScenesに並べたシーンでは自分のキャラクターでも動かせない。
/// (PlayerCharacterVisualの「非表示にするシーン」と同じく、指定した以外は動かせる方式)
///
/// オンライン接続していない状態(Test_Playerシーンなど、prefabを直接置いて試す場合)では
/// 何も判定せず、そのまま操作できるようにしてある。
/// </summary>
public class PlayerOwnerControl : NetworkBehaviour
{
    [Header("移動できないシーン名(自分のキャラクターでも止める)")]
    [SerializeField]
    private string[] movementDisabledScenes =
    {
        "01_Title",
        "02_PlayerJoin",
        "03_StageSelect",
    };

    // PlayerMovementが「走っているか」を書き込むAnimatorのパラメータ名(PlayerMovement.csと同じ名前にすること)
    private static readonly int RunningHash = Animator.StringToHash("IsRunning");

    // 走っているかどうか。操作している本人のPCだけが書き込み、他の人の画面のアニメーションに反映する。
    // (位置はNetworkTransformが送るが、Animatorのパラメータは自動では送られないため)
    private readonly NetworkVariable<bool> isRunning = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private PlayerInput playerInput;
    private PlayerMovement playerMovement;
    private CharacterController characterController;
    private Animator animator;

    // このPCのこのキャラクターを、このPCの入力で操作してよいか
    private bool isLocallyControlled;

    // 直近に操作の有効/無効を判定したときのシーン名。
    // シーンが切り替わったことを、イベントだけに頼らずUpdateでも検知するために覚えておく。
    private string lastEvaluatedSceneName;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        playerMovement = GetComponent<PlayerMovement>();
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        // オンラインで生成されたキャラクターは、Startの時点で既にOnNetworkSpawnが終わっている。
        // まだスポーンしておらず、ネットワークも動いていないなら、単体で置かれたキャラクター
        // (Test_Playerなど)なので、そのまま操作できるようにする。
        bool networkRunning = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!IsSpawned && !networkRunning)
        {
            isLocallyControlled = true;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            RefreshControlState();
        }
    }

    public override void OnNetworkSpawn()
    {
        // シーンに最初から置かれているキャラクター(Test_Playerの試験用など)は所有者がサーバーになるが、
        // 誰かが操作するものではないので、操作対象にはしない。接続して生成されたキャラクターだけが対象。
        isLocallyControlled = IsOwner && !NetworkObject.InScenePlaced;

        isRunning.OnValueChanged += OnRunningChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        RefreshControlState();

        // 後から参加した人にとっては、既に走っている最中の可能性があるので、現在値を反映しておく
        ApplyRunningToAnimator(isRunning.Value);
    }

    public override void OnNetworkDespawn()
    {
        isRunning.OnValueChanged -= OnRunningChanged;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    public override void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        base.OnDestroy();
    }

    private void Update()
    {
        // activeSceneChangedイベントを取りこぼしても、シーンが変わっていれば判定し直す。
        // (特に、後から参加した側のPCで、ネットワーク経由のシーン切り替え時にイベントが
        //  期待どおり来ない場合の保険)
        if (SceneManager.GetActiveScene().name != lastEvaluatedSceneName)
        {
            RefreshControlState();
        }

        // 操作している本人のPCだけ、PlayerMovementが決めた「走っているか」を他の人へ送る
        if (!IsSpawned || !IsOwner || animator == null) return;

        bool running = animator.GetBool(RunningHash);
        if (isRunning.Value != running)
        {
            isRunning.Value = running;
        }
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        RefreshControlState();
    }

    /// <summary>
    /// 「自分が操作するキャラクターか」と「今のシーンで動かしてよいか」に応じて、
    /// 入力・移動まわりのコンポーネントの有効/無効を切り替える。
    /// </summary>
    private void RefreshControlState()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        lastEvaluatedSceneName = sceneName;
        bool movementAllowed = isLocallyControlled && !IsMovementDisabledInScene(sceneName);

        string localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId.ToString() : "offline";
        Debug.Log($"[PlayerOwnerControl] thisPC={localId} owner={(IsSpawned ? OwnerClientId.ToString() : "-")} " +
            $"locallyControlled={isLocallyControlled} scene={sceneName} movementAllowed={movementAllowed}");

        if (movementAllowed)
        {
            // 順番が大事: PlayerMovementは有効になる時にPlayerInputの入力設定を取りに行くので、
            // 先にPlayerInput(と移動の土台のCharacterController)を有効にする。
            if (characterController != null) characterController.enabled = true;
            if (playerInput != null) playerInput.enabled = true;
            if (playerMovement != null) playerMovement.enabled = true;
        }
        else
        {
            if (playerMovement != null) playerMovement.enabled = false;
            if (playerInput != null) playerInput.enabled = false;
            if (characterController != null) characterController.enabled = false;

            // 走っている途中で止めた場合に、走りアニメーションが残らないようにする
            if (isLocallyControlled && animator != null) animator.SetBool(RunningHash, false);
        }
    }

    private bool IsMovementDisabledInScene(string sceneName)
    {
        foreach (string disabledScene in movementDisabledScenes)
        {
            if (disabledScene == sceneName) return true;
        }

        return false;
    }

    private void OnRunningChanged(bool previousValue, bool newValue)
    {
        // 自分のキャラクターはPlayerMovementが直接Animatorを動かしているので、他の人の分だけ反映する
        if (IsOwner) return;

        ApplyRunningToAnimator(newValue);
    }

    private void ApplyRunningToAnimator(bool running)
    {
        if (IsOwner || animator == null) return;

        animator.SetBool(RunningHash, running);
    }
}
