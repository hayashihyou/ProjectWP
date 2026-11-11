using UnityEngine;

/// <summary>
/// プレイヤーのボール関係のアニメーション。プレイヤーの一番上(Animator と同じオブジェクト)に付ける。
/// ・持っているか(IsHolding)は、ボールの状態から毎フレーム決める(全員の画面で同じになる)
/// ・投げる(Throw)は、サーバーからの「投げる」の出来事で再生する
/// ・ボールを付ける手の位置を、ボールに教える
/// </summary>
public class PlayerBallAnimation : MonoBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int ThrowHash = Animator.StringToHash("Throw");
    private static readonly int IsHoldingHash = Animator.StringToHash("IsHolding");

    [SerializeField] private Animator animator;

    [Tooltip("ボールを持たせる手(右手のボーン)")]
    [SerializeField] private Transform rigidHand;

    private PlayerSlot playerSlot;
    private Vector3 lastPos;

    // ミニゲームが終わるとイベントの登録が消えるので、ミニゲームが始まるたびに登録し直す
    private bool isSubscribed;

    /// <summary>ボールを持たせる位置</summary>
    public Transform Hand => rigidHand != null ? rigidHand : transform;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        playerSlot = GetComponentInParent<PlayerSlot>();
    }

    private void OnEnable()
    {
        lastPos = transform.position;
        isSubscribed = false;
    }

    private void OnDisable()
    {
        MinigameInfo.OnCustomEvent -= HandleCustomEvent;
        isSubscribed = false;
    }

    private void Update()
    {
        UpdateSubscription();

        if (animator == null) return;

        // 移動速度を Animator に渡す(地面方向の移動のみ。ほかの人のキャラクターでも動く)
        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;
        lastPos = transform.position;
        if (Time.deltaTime > 0f)
        {
            animator.SetFloat(SpeedHash, delta.magnitude / Time.deltaTime);
        }

        // ボールを持っているか
        int slot = playerSlot != null ? playerSlot.SlotIndex : -1;
        animator.SetBool(IsHoldingHash, slot >= 0 && ButsukekkoBall.IsHeldBy(slot));
    }

    private void UpdateSubscription()
    {
        if (MinigameInfo.IsActive)
        {
            if (!isSubscribed)
            {
                MinigameInfo.OnCustomEvent -= HandleCustomEvent; // 二重登録しないように一度外す
                MinigameInfo.OnCustomEvent += HandleCustomEvent;
                isSubscribed = true;
            }
        }
        else
        {
            // ミニゲームが終わった時点で登録は消えているので、次のミニゲームで登録し直す
            isSubscribed = false;
        }
    }

    // サーバーからの出来事。自分のキャラクターが投げ始めたら、投げるアニメーションを再生する
    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
    {
        if (eventName != ButsukekkoMinigame.HitEventName) return;
        if (playerSlot == null || slot != playerSlot.SlotIndex) return;
        if (animator == null) return;

        animator.SetTrigger(ThrowHash);
    }
}