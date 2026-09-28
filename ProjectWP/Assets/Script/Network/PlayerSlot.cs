using System.Collections;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

/// <summary>
/// このキャラクターが「1P〜4Pのどの枠か」を管理する。
/// スポーンした瞬間にPlayerSlotManagerから空いている枠を1つもらい、
/// その枠の立ち位置に移動する。ネットワークから抜けたら枠を返す。
///
/// 枠の番号(SlotIndex)はNetworkVariableで全クライアントに配信されるので、
/// 「このキャラクターはUI上の何P目か」を誰の画面でも同じように判定できる。
/// 枠が決まったら、その枠に対応する色を(全クライアントのそれぞれの画面で)
/// 見た目に反映する。
///
/// 注意: ホスト自身のキャラクターは、01_Titleでホストを開始した直後
/// (=まだ02_PlayerJoinシーンが読み込まれておらず、PlayerSlotManagerが
/// 存在しないタイミング)にスポーンする。そのため、PlayerSlotManagerが
/// 現れるまで待ってから枠を確保するようにしている。
/// </summary>
[RequireComponent(typeof(PlayerCharacterVisual))]
public class PlayerSlot : NetworkBehaviour
{
    [Header("枠ごとの色")]
    [Tooltip("先頭から1P, 2P, 3P, 4Pの順。1Pは変更しない(白=元の色のまま)")]
    [SerializeField]
    private Color[] slotColors =
    {
        Color.white,                    // 1P: 変更なし
        new Color(1f, 0.55f, 0f),       // 2P: オレンジ
        new Color(0.4f, 0.9f, 1f),      // 3P: 水色
        new Color(0.6f, 1f, 0.2f),      // 4P: 黄緑
    };

    // 自分がどの枠か(0=1P, 1=2P, ...)。-1はまだ割り当てられていない。
    private readonly NetworkVariable<int> slotIndex = new NetworkVariable<int>(-1);

    public int SlotIndex => slotIndex.Value;

    private PlayerCharacterVisual visual;
    private NetworkTransform networkTransform;

    private void Awake()
    {
        visual = GetComponent<PlayerCharacterVisual>();
        networkTransform = GetComponent<NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        // 枠が決まったら(=slotIndexの値が変わったら)色を反映する。
        // これはサーバー・クライアントどちらでも実行する(自分の画面にも
        // 相手の画面にも同じ色が見えるようにするため)。
        slotIndex.OnValueChanged += OnSlotIndexChanged;

        if (IsServer)
        {
            ClaimSlotWhenManagerReady();
        }

        // 自分がこのオブジェクトを見た時点で、既に枠が決まっていれば
        // (サーバー自身や、後から見た人にとってはそうなる)、すぐに色を反映する。
        if (slotIndex.Value >= 0)
        {
            ApplySlotColor(slotIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        slotIndex.OnValueChanged -= OnSlotIndexChanged;

        if (!IsServer) return;
        if (slotIndex.Value < 0) return;

        if (PlayerSlotManager.Instance != null)
        {
            PlayerSlotManager.Instance.ReleaseSlot(slotIndex.Value);
        }
    }

    private void ClaimSlotWhenManagerReady()
    {
        // PlayerSlotManagerは02_PlayerJoinシーンにしかいない。
        // ホスト自身のキャラクターはStartHost()直後(まだ01_Titleにいる間)に
        // スポーンするので、この時点ではまだ見つからない。
        // シーン遷移でPlayerSlotManagerが現れるまで待ってから確保する。
        StartCoroutine(WaitForSlotManagerAndClaim());
    }

    private IEnumerator WaitForSlotManagerAndClaim()
    {
        // 1フレーム待ってから始める。スポーンした直後に、所有者へ「ここに立って」と
        // 伝えるRPCを送ると、相手の画面にキャラクターが出来る前に届いてしまうことがあるため。
        yield return null;

        while (PlayerSlotManager.Instance == null)
        {
            yield return null;
        }

        ClaimSlotNow();
    }

    private void ClaimSlotNow()
    {
        if (PlayerSlotManager.Instance.TryClaimSlot(out int claimedSlot))
        {
            slotIndex.Value = claimedSlot;

            // 位置はNetworkTransformが「所有者主導」で同期している(操作する本人のPCが位置を決める)ため、
            // サーバーが直接transformを書き換えても、本人のPCの位置で上書きされて効かない。
            // そこで、所有者(ホスト自身のキャラならこのPC)に「ここに立って」と頼む。
            Vector3 slotPosition = PlayerSlotManager.Instance.GetSpawnPosition(claimedSlot);
            TeleportToSlotRpc(slotPosition, GetRotationFacingCamera(slotPosition));

            // シーン移動をまたいでスポーンしたキャラクター(ホスト自身など)は、
            // 移動の影響でアニメーションの再生が失われていることがあるため、
            // 「シーン移動が完了して枠が確保できた」このタイミングで念のため再生し直す。
            if (visual != null) visual.ReapplyCurrentAnimation();
        }
        else
        {
            // 4人分の枠が全部埋まっている(想定外の人数)場合。
            // ConnectionapprovalhandlerでmaxTotalPlayersを4に制限しているので、
            // 通常は起きないはずだが、念のためログだけ残す。
            Debug.LogWarning("[PlayerSlot] No free slot available.");
        }
    }

    /// <summary>
    /// 指定した位置に立ったときの、メインカメラの方を向く向きを返す。
    /// (見上げ/見下ろしにならないよう、高さ(Y)は立つ位置に合わせてから向きを計算する)
    /// </summary>
    private Quaternion GetRotationFacingCamera(Vector3 standPosition)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return transform.rotation;

        Vector3 lookTarget = mainCamera.transform.position;
        lookTarget.y = standPosition.y;

        // 位置がほぼ同じ(真上/真下など)だとLookRotationが不定になるので念のためガード
        if ((lookTarget - standPosition).sqrMagnitude < 0.0001f) return transform.rotation;

        return Quaternion.LookRotation(lookTarget - standPosition);
    }

    /// <summary>
    /// 所有者のPCで、この位置・向きに一瞬で移動する。
    /// Teleportを使うのは、補間(なめらかな移動)をせず、全員の画面でも即座に
    /// その位置に現れるようにするため。
    /// </summary>
    [Rpc(SendTo.Owner)]
    private void TeleportToSlotRpc(Vector3 position, Quaternion rotation)
    {
        if (networkTransform == null) return;

        // CharacterControllerが有効なままtransformを書き換えると、次の移動で元の位置に
        // 戻されてしまうことがあるため、移動している間だけ無効にする。
        CharacterController characterController = GetComponent<CharacterController>();
        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled) characterController.enabled = false;

        networkTransform.Teleport(position, rotation, transform.localScale);

        if (wasEnabled) characterController.enabled = true;
    }

    /// <summary>
    /// 別のシーンの初期位置などへ、所有者のPCで一瞬で移動させる(サーバーから呼ぶ)。
    /// </summary>
    public void MoveTo(Vector3 position, Quaternion rotation)
    {
        TeleportToSlotRpc(position, rotation);
    }

    private void OnSlotIndexChanged(int previousValue, int newValue)
    {
        ApplySlotColor(newValue);
    }

    private void ApplySlotColor(int index)
    {
        if (visual == null) return;
        if (index < 0 || index >= slotColors.Length) return;

        visual.ApplyColor(slotColors[index]);
    }
}
