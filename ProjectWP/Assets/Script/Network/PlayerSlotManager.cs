using UnityEngine;

/// <summary>
/// 02_PlayerJoinシーンにある「1P〜4Pの枠」を管理するクラス。
/// シーンに直接1つだけ配置して使う(GameSessionStateと同じやり方)。
///
/// キャラクターがスポーンしたら空いている枠を1つ割り当て、
/// 抜けたら枠を空ける。実際にどの位置に立たせるかもここで持っておく。
/// 枠の割り当て・解放はサーバーだけが行う(PlayerSlot.csから呼ばれる)。
///
/// これ自体はネットワーク越しに同期する値を持たない(あくまでサーバー内部だけの
/// 帳簿)ので、NetworkBehaviourではなく普通のMonoBehaviourにしてある。
/// </summary>
public class PlayerSlotManager : MonoBehaviour
{
    public static PlayerSlotManager Instance { get; private set; }

    public const int SlotCount = 4;

    [Tooltip("1P〜4Pに対応する、キャラクターを立たせる位置。この順番のまま4つ設定すること。" +
        "空欄の場合はslotPositions(コード内の決め打ち座標)を使う。")]
    [SerializeField] private Transform[] spawnPoints = new Transform[SlotCount];

    // 1P〜4Pの立ち位置(ワールド座標)。決め打ち。
    //
    // 画面上の「1P」〜「4P」のUI枠(02_PlayerJoinのSlotFrame1〜4、Canvas上で
    // 横方向に -615/-205/205/615 ピクセルの位置、Screen Space - Camera)の真下に
    // 見えるように、Main Cameraの設定(position (0,1,-8)、垂直FOV 60度)と
    // キャラクターを置く奥行き(ワールドZ=0、カメラから8ユニット)から逆算した値。
    // 計算式: X = ピクセル位置 × (2 × 奥行き × tan(垂直FOV/2)) ÷ 画面の高さ(ピクセル)
    //   例: -615px × (2×8×tan(30°)) ÷ 1080px ≒ -5.26
    // 画面の高さ1080px(フルスクリーン時の一般的なモニタ解像度)を前提にしているため、
    // 実際の画面の高さがこれと大きく違う場合はズレる。その場合はここの数値を
    // 「画面の高さの比」で掛け直すか、見た目を見ながら直接調整すること。
    // ※SlotFrame1〜4の位置・サイズを変えた場合は、この値も一緒に計算し直すこと
    //   (UI側だけ変えてこちらを直し忘れるとズレる。実際に一度それで事故った)。
    private static readonly Vector3[] slotPositions =
    {
        new Vector3(-5.26f, -0.09f, 0f), // 1P
        new Vector3(-1.75f, -0.09f, 0f), // 2P
        new Vector3(1.75f, -0.09f, 0f),  // 3P
        new Vector3(5.26f, -0.09f, 0f),  // 4P
    };

    // 各枠が埋まっているかどうか。サーバーだけが読み書きする。
    private readonly bool[] occupiedSlots = new bool[SlotCount];

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 空いている枠を1つ確保する。サーバーだけが呼ぶ想定。
    /// </summary>
    /// <param name="slotIndex">確保できた枠の番号(0=1P, 1=2P, ...)。確保できなければ-1。</param>
    /// <returns>確保できたらtrue。満員ならfalse。</returns>
    public bool TryClaimSlot(out int slotIndex)
    {
        for (int i = 0; i < occupiedSlots.Length; i++)
        {
            if (!occupiedSlots[i])
            {
                occupiedSlots[i] = true;
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// 使い終わった枠を空ける。サーバーだけが呼ぶ想定。
    /// </summary>
    public void ReleaseSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= occupiedSlots.Length) return;
        occupiedSlots[slotIndex] = false;
    }

    /// <summary>
    /// 指定した枠のキャラクター立ち位置を返す。
    /// シーンにTransformが設定されていればそれを優先し(手で微調整したい場合用)、
    /// 無ければコード内の決め打ち座標(slotPositions)を使う。
    /// </summary>
    public Vector3 GetSpawnPosition(int slotIndex)
    {
        if (spawnPoints != null && slotIndex >= 0 && slotIndex < spawnPoints.Length && spawnPoints[slotIndex] != null)
        {
            return spawnPoints[slotIndex].position;
        }

        if (slotIndex >= 0 && slotIndex < slotPositions.Length)
        {
            return slotPositions[slotIndex];
        }

        return new Vector3(slotIndex * 3f, 0f, 0f);
    }
}
