using UnityEngine;

/// <summary>
/// みんなでぶつけっこの点数を、画面の端の点数UI(PointCounter)と、机の上のスコア板(ButsukekkoScoreBoard)に出す。
/// MG_Butsukekko の ScoreCanvas(画面の端の点数UI)に付ける。
///
/// 点数が変わると、全員の端末で MinigameInfo.OnPlayerValueChanged が呼ばれるので、それを聞いて書き換えるだけ。
/// 点数そのものは ButsukekkoMinigame(サーバー)が決めている。
/// プレイヤーがいない枠のUIは隠す。
///
/// 隠すときは SetActive(false) ではなく、CanvasGroup で透明にする。
/// (背景の角丸を描く ProceduralUIComponent は、一度オフ→オンにすると角丸の見た目が戻らず、白い四角になってしまうため)
/// </summary>
public class ButsukekkoScoreUI : MonoBehaviour
{
    [Tooltip("画面の端の点数の文字。上から 1P, 2P, 3P, 4P の順")]
    [SerializeField] private PointCounter[] counters = new PointCounter[4];

    [Tooltip("いない人の枠を隠すときに消すもの(Player1〜Player4 のパネル)。上から 1P, 2P, 3P, 4P の順")]
    [SerializeField] private GameObject[] panels = new GameObject[4];

    // panels を透明にするためのもの(Awake で panels に付ける)
    private CanvasGroup[] panelGroups = new CanvasGroup[0];

    // 机の上のスコア板(シーンから自動で探す。ステージのプレハブの中にあるため)
    private ButsukekkoScoreBoard[] boards = new ButsukekkoScoreBoard[0];

    private void Awake()
    {
        panelGroups = new CanvasGroup[panels.Length];
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] == null) { continue; }
            panelGroups[i] = panels[i].GetComponent<CanvasGroup>();
            if (panelGroups[i] == null) { panelGroups[i] = panels[i].AddComponent<CanvasGroup>(); }
        }
    }

    private void OnEnable()
    {
        MinigameInfo.OnPlayerValueChanged += HandlePlayerValueChanged;
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPlayerValueChanged -= HandlePlayerValueChanged;
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
    }

    private void Start()
    {
        boards = FindObjectsByType<ButsukekkoScoreBoard>(FindObjectsSortMode.None);
        ShowAllScores();
    }

    // 誰かの点数が変わった → その人の数字を書き換える
    private void HandlePlayerValueChanged(int slot, float delta, float newValue)
    {
        SetScore(slot, Mathf.RoundToInt(newValue));
    }

    // 段階が変わった(参加者がそろった・ラウンドが始まった など) → 全員分を出し直す
    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        ShowAllScores();
    }

    // 今いる人の点数を全部出し直す。いない人の枠は隠す
    private void ShowAllScores()
    {
        for (int slot = 0; slot < panelGroups.Length; slot++)
        {
            bool joined = MinigameInfo.IsActive && MinigameInfo.TryGetPlayerBySlot(slot, out _);
            if (panelGroups[slot] != null) { panelGroups[slot].alpha = joined ? 1f : 0f; }
        }

        if (!MinigameInfo.IsActive) { return; }
        foreach (MinigamePlayerState player in MinigameInfo.Players)
        {
            SetScore(player.SlotIndex, Mathf.RoundToInt(player.RoundValue));
        }
    }

    private void SetScore(int slot, int score)
    {
        if (slot >= 0 && slot < counters.Length && counters[slot] != null)
        {
            counters[slot].SetCount(score);
        }

        foreach (ButsukekkoScoreBoard board in boards)
        {
            if (board != null && board.SlotIndex == slot) { board.SetScore(score); }
        }
    }
}
