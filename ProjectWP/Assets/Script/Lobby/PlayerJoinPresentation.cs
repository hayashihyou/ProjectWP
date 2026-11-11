using System.Collections;
using UnityEngine;

/// <summary>
/// 待機所(02_PlayerJoin)に1つだけ置く、「参加したときの演出」の取りまとめ役。
///
/// PlayerSlot.Joined(枠が決まった瞬間に、全員の画面でそれぞれ呼ばれるイベント)を聞いて、
/// 同じGameObjectに付いている演出(PlayerJoinPresenterBase)のうち、有効なものをすべて再生する。
/// 演出はネットワーク同期しない(各PCが自分の画面にだけ出す)。
///
/// テスト用に「参加をもう一度再生する」機能も持っていて、EffectReplayRegistry に登録しておくと
/// EffectTester のパネルからボタンで呼べる(参加音・演出・キャラクターの出現をまとめて再生し直す)。
/// </summary>
public class PlayerJoinPresentation : MonoBehaviour
{
    // 参加音(PlayerSlotが鳴らしているものと同じID)。もう一度再生するときに使う
    private const string JoinSoundId = "Common_PlayerJoin";

    // テスト用パネルでの見出し
    private const string ReplayGroup = "待機所: 参加演出";

    [Tooltip("枠の色が白(1P)のときに、代わりに使う色。白のままだと明るい背景に溶けて見えにくいため。")]
    [SerializeField] private Color whiteSlotReplacement = new Color(1f, 0.85f, 0.35f);

    [Tooltip("もう一度再生するとき、キャラクターを消してから演出を始めるまでの秒数(「いない状態」を一瞬見せるため)")]
    [SerializeField] private float replayHiddenSeconds = 0.4f;

    private PlayerJoinPresenterBase[] presenters;

    private void Awake()
    {
        presenters = GetComponents<PlayerJoinPresenterBase>();
    }

    private void OnEnable()
    {
        PlayerSlot.Joined += OnPlayerJoined;
        RegisterReplayEntries();
    }

    private void OnDisable()
    {
        PlayerSlot.Joined -= OnPlayerJoined;
        EffectReplayRegistry.Unregister(this);
    }

    private void OnPlayerJoined(PlayerSlot slot)
    {
        Present(slot.SlotIndex, slot.SlotColor, slot);
    }

    /// <summary>
    /// 有効(チェックが入っている)な演出をすべて再生する。
    /// </summary>
    public void Present(int slotIndex, Color slotColor, PlayerSlot slot)
    {
        Color color = IsAlmostWhite(slotColor) ? whiteSlotReplacement : slotColor;

        foreach (PlayerJoinPresenterBase presenter in presenters)
        {
            if (presenter != null && presenter.enabled)
            {
                presenter.Present(slotIndex, color, slot);
            }
        }
    }

    // ==================== テスト用: もう一度再生 ====================

    /// <summary>
    /// 【テスト用】指定した枠の「参加」を、自分の画面だけでもう一度再生する。
    /// その枠にキャラクターがいれば一度消してから出し直す。いなければ演出と音だけ再生する。
    /// </summary>
    public void Replay(int slotIndex)
    {
        StartCoroutine(ReplayRoutine(slotIndex));
    }

    private IEnumerator ReplayRoutine(int slotIndex)
    {
        PlayerSlot slot = FindSlot(slotIndex);
        PlayerCharacterVisual visual = slot != null ? slot.GetComponent<PlayerCharacterVisual>() : null;

        // 見た目だけを一時的に消す(既存の SetHiddenTemporarily を使うので、キャラクター側のコードは変えない)
        if (visual != null)
        {
            visual.SetHiddenTemporarily(true);
            yield return new WaitForSeconds(replayHiddenSeconds);
            visual.SetHiddenTemporarily(false);
        }

        SoundManager.Instance.PlaySe(JoinSoundId);

        Color slotColor = slot != null ? slot.SlotColor : GetDefaultSlotColor(slotIndex);
        Present(slotIndex, slotColor, slot);
    }

    private static PlayerSlot FindSlot(int slotIndex)
    {
        foreach (PlayerSlot slot in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            if (slot.SlotIndex == slotIndex) return slot;
        }
        return null;
    }

    // キャラクターがいない枠の色。PlayerSlotの初期値と同じ色にしてある(テスト用なので決め打ち)
    private static Color GetDefaultSlotColor(int slotIndex)
    {
        switch (slotIndex)
        {
            case 1: return new Color(0.75f, 0.6f, 1f);
            case 2: return new Color(0.4f, 0.9f, 1f);
            case 3: return new Color(0.6f, 1f, 0.2f);
            default: return Color.white;
        }
    }

    /// <summary>テスト用パネル(EffectTester)に「1P〜4Pの参加を再生」ボタンを登録する</summary>
    private void RegisterReplayEntries()
    {
        for (int i = 0; i < PlayerSlotManager.SlotCount; i++)
        {
            int slotIndex = i; // ラムダの中で使うので、ループ変数をコピーしておく
            EffectReplayRegistry.RegisterButton(this, ReplayGroup, $"{slotIndex + 1}P の参加を再生", () => Replay(slotIndex));
        }
    }

    private static bool IsAlmostWhite(Color color)
    {
        return color.r > 0.95f && color.g > 0.95f && color.b > 0.95f;
    }
}
