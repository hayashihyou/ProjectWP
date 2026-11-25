using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 参加演出「スポットライト」。
/// キャラクターが立っている台座に、下から白いスポットライト(PlayerSpotlight)を当てる。
///
/// 点くタイミング:
///   ・参加した瞬間(Present)は、煙ポンが終わってから点く(lightDelay 秒待つ)。
///   ・後から待機所に入った人の画面では、もういる人の「参加の瞬間」は来ない。
///     そこで、定期的に「今いるキャラクター」を確認して、ライトが無ければすぐ点ける。
/// 消えるタイミング:
///   ・キャラクターが退出して消えたら、ふわっと消す。
///
/// キャラクター側のプレハブやスクリプトは変更しない(キャラクターを探して見るだけ)。
/// </summary>
public class PlayerJoinSpotlight : PlayerJoinPresenterBase
{
    [Tooltip("台座に出すスポットライト")]
    [SerializeField] private PlayerSpotlight spotlightPrefab;

    [Tooltip("立ち位置(スポーン位置)からのずらし量。台座の上面に合わせるのに使う。")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, -0.29f, 0f);

    [Tooltip("参加してから点くまでの秒数。煙のエフェクトが消え切るころに合わせる。")]
    [SerializeField] private float lightDelay = 1f;

    [Tooltip("今いるキャラクターを確認する間隔(秒)")]
    [SerializeField] private float checkInterval = 0.2f;

    /// <summary>台座に出すスポットライトのプレハブ(テスト用パネルが形の初期値を読むのに使う)</summary>
    public PlayerSpotlight SpotlightPrefab => spotlightPrefab;

    /// <summary>立ち位置からのずらし量(テスト用パネルが同じ場所に出すのに使う)</summary>
    public Vector3 PositionOffset => positionOffset;

    /// <summary>枠1つ分のライトの状態</summary>
    private class SlotLight
    {
        public PlayerSlot Slot;          // 照らしているキャラクター
        public PlayerSpotlight Light;    // 出しているライト
        public float WaitSeconds;        // 点くまでの残り秒数(0以下なら点いている)
    }

    // 枠の番号(0=1P, ...) → その枠のライト
    private readonly Dictionary<int, SlotLight> slotLights = new Dictionary<int, SlotLight>();
    private readonly List<int> removeList = new List<int>();
    private float checkTimer;

    public override void Present(int slotIndex, Color color, PlayerSlot slot)
    {
        // キャラクターがいないとき(テストでいない枠を再生したとき)は照らさない
        if (slot == null) return;

        SlotLight slotLight = GetOrCreate(slotIndex, slot);

        // 煙が出ている間は消しておき、煙が終わったら点ける
        // (もう一度再生したときも、一度消してから点け直す)
        slotLight.Light.HideImmediately();
        slotLight.WaitSeconds = lightDelay;
    }

    private void Update()
    {
        // 点くのを待っているライトを数える
        foreach (SlotLight slotLight in slotLights.Values)
        {
            if (slotLight.WaitSeconds <= 0f) continue;

            slotLight.WaitSeconds -= Time.deltaTime;
            if (slotLight.WaitSeconds <= 0f)
            {
                slotLight.Light.TurnOn();
            }
        }

        checkTimer -= Time.deltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = checkInterval;
            CheckSlots();
        }
    }

    // 今いるキャラクターとライトを見比べて、足りないライトを点け、いらないライトを消す
    private void CheckSlots()
    {
        // 退出した(キャラクターが消えた・枠が外れた)ライトを消す
        removeList.Clear();
        foreach (KeyValuePair<int, SlotLight> pair in slotLights)
        {
            SlotLight slotLight = pair.Value;
            bool left = slotLight.Slot == null || slotLight.Slot.SlotIndex != pair.Key;
            if (left || slotLight.Light == null)
            {
                if (slotLight.Light != null) slotLight.Light.TurnOffAndDestroy();
                removeList.Add(pair.Key);
            }
        }
        foreach (int slotIndex in removeList)
        {
            slotLights.Remove(slotIndex);
        }

        // もういるのにライトが無いキャラクター(後から待機所に入った人の画面など)は、すぐ点ける
        foreach (PlayerSlot slot in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            if (slot.SlotIndex < 0) continue;
            if (slotLights.ContainsKey(slot.SlotIndex)) continue;

            SlotLight slotLight = GetOrCreate(slot.SlotIndex, slot);
            slotLight.Light.TurnOn();
        }
    }

    private SlotLight GetOrCreate(int slotIndex, PlayerSlot slot)
    {
        if (slotLights.TryGetValue(slotIndex, out SlotLight slotLight) && slotLight.Light != null)
        {
            slotLight.Slot = slot;
            return slotLight;
        }

        Vector3 position = PlayerSlotManager.Instance != null
            ? PlayerSlotManager.Instance.GetSpawnPosition(slotIndex) + positionOffset
            : slot.transform.position;

        // このオブジェクトの子にしておく(シーンが変わったら一緒に消える)
        PlayerSpotlight light = Instantiate(spotlightPrefab, position, Quaternion.identity, transform);

        // どの枠のライトかを教える(「プレイヤーごとの色」のとき、その枠の色になる)
        light.SetSlotIndex(slotIndex);

        slotLight = new SlotLight { Slot = slot, Light = light, WaitSeconds = 0f };
        slotLights[slotIndex] = slotLight;
        return slotLight;
    }
}
