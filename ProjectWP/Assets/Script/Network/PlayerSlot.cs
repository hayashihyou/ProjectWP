using System.Collections;
using UnityEngine;
using Unity.Netcode;

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

    private void Awake()
    {
        visual = GetComponent<PlayerCharacterVisual>();
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
        if (PlayerSlotManager.Instance != null)
        {
            ClaimSlotNow();
            return;
        }

        // PlayerSlotManagerは02_PlayerJoinシーンにしかいない。
        // ホスト自身のキャラクターはStartHost()直後(まだ01_Titleにいる間)に
        // スポーンするので、この時点ではまだ見つからない。
        // シーン遷移でPlayerSlotManagerが現れるまで待ってから確保する。
        StartCoroutine(WaitForSlotManagerAndClaim());
    }

    private IEnumerator WaitForSlotManagerAndClaim()
    {
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
            transform.position = PlayerSlotManager.Instance.GetSpawnPosition(claimedSlot);
            FaceCamera();

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
    /// メインカメラの方を向かせる(見上げ/見下ろしにならないよう、
    /// 高さ(Y)は自分の位置に合わせてから向きを計算する)。
    /// 位置と同じくサーバーだけで計算し、NetworkTransformで全クライアントに同期される。
    /// </summary>
    private void FaceCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        Vector3 lookTarget = mainCamera.transform.position;
        lookTarget.y = transform.position.y;

        // 位置がほぼ同じ(真上/真下など)だとLookRotationが不定になるので念のためガード
        if ((lookTarget - transform.position).sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(lookTarget - transform.position);
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
