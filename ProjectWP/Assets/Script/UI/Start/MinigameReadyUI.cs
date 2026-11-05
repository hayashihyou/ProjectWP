using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 準備確認(全員が決定ボタンで「準備OK」を押すまで待つ)の表示。ReadyUI プレハブの一番上に付いている。
/// どのミニゲームのシーンにも、プレハブ(Prefabs/UI/MinigameReadyUI)を置くだけで使える。ミニゲームごとの設定はいらない
///
/// ・このスクリプトは表示するだけ。準備OKの集計はサーバー(MinigameBase)がして、押したことは MinigameInfo のイベントで届く
/// ・ボタンの入力は PlayerMinigameInput(決定ボタン = Confirm)が MinigameInfo.SendReady で送る
/// ・MinigameBase の Require Player Ready がオフなら、ずっと表示されない
/// ・各プレイヤーのボタンは、そのキャラクター(PlayerSlot)の上についていく。キャラクターは全員の端末に同期されているので、
///   ホストにも参加者にも、同じ人の上に同じボタンが見える
///
/// 流れ
///   Phase が ReadyCheck になる → 背景を少し暗くして、いる人のボタンをそれぞれのキャラクターの上に出す
///   誰かが押す                 → その人のボタンを「準備中」→「OK!」に切り替え、弾ませて光らせる
///   全員そろう                 → ScreenFader でフェードアウトして消す(消えた後にスタート)
/// </summary>
public class MinigameReadyUI : MonoBehaviour
{
    /// <summary>ボタンをどこに置くか</summary>
    public enum Placement
    {
        FollowPlayer,   // それぞれのキャラクターの上(キャラクターが見つからない人は、下の横並びの位置)
        CenterRow,      // いる人だけを画面の真ん中に横並び
        AsPlaced        // プレハブに置いた位置のまま
    }

    [Serializable]
    private class PlayerPanel
    {
        [Tooltip("この枠(1P〜4P)のパネル全体。その枠の人がいなければ隠す")]
        public GameObject root;

        [Tooltip("「準備中」→「OK!」の切り替え")]
        public ReadyTextSwitcher textSwitcher;

        [Tooltip("準備OKになったときに弾ませる")]
        public ButtonPunchScale punchScale;

        [Tooltip("準備OKになったときに光らせる")]
        public ButtonWhiteFlash whiteFlash;

        [Tooltip("動かす中身(ボタンの見た目)。空なら動かさない")]
        public RectTransform content;

        [NonSerialized] public bool shownReady;    // もう「OK!」にしたか
        [NonSerialized] public float baseContentX; // 置いたときの横の位置
        [NonSerialized] public Vector2 rowPosition; // 横並びにしたときの位置(キャラクターが見つからないときに使う)
        [NonSerialized] public PlayerSlot player;  // ついていくキャラクター
    }

    [Header("表示")]
    [Tooltip("準備確認の間だけ表示する Canvas。最初は非表示にしておく")]
    [SerializeField] private GameObject canvasRoot;

    [Tooltip("全員そろったら画面を消すフェード(Canvas の ScreenFader)。空なら一瞬で消す")]
    [SerializeField] private ScreenFader fader;

    [Tooltip("1P, 2P, 3P, 4P の順のパネル")]
    [SerializeField] private PlayerPanel[] panels = new PlayerPanel[4];

    [Header("ボタンの位置")]
    [Tooltip("FollowPlayer = それぞれのキャラクターの上 / CenterRow = 画面の真ん中に横並び / AsPlaced = 置いた位置のまま")]
    [SerializeField] private Placement placement = Placement.FollowPlayer;

    [Tooltip("キャラクターの足元から、どこにボタンを出すか(ワールドの距離。Y を大きくすると上へ)")]
    [SerializeField] private Vector3 playerWorldOffset = new Vector3(0f, 2.0f, 0f);

    [Tooltip("画面上でさらにずらす量(ピクセル。Canvas の基準の大きさで)")]
    [SerializeField] private Vector2 screenOffset = Vector2.zero;

    [Tooltip("ついていくカメラ。空なら MainCamera")]
    [SerializeField] private Camera targetCamera;

    [Header("音")]
    [Tooltip("誰かが準備OKになったときに鳴らす音(SoundLibrary のID)。空欄なら鳴らさない")]
    [SerializeField] private string readySoundId = "Common_Ready";


    // 参加者の端末では、フェーズの合図(イベント)が値(MinigameInfo.Phase)より先に届く。
    // 表示してからこの秒数の間は、値がまだ前のフェーズのままでも消さない
    private const float PhaseSyncGraceSeconds = 2f;

    private bool shown;   // 表示中か
    private bool hiding;  // 消す演出中か
    private float shownTime; // 表示した時刻
    private bool pendingShow; // 出す時刻(サーバー時刻)を待っている
    private bool pendingHide; // 消し始める時刻(サーバー時刻)を待っている
    private readonly List<int> visibleSlots = new List<int>();
    private float nextPlayerSearchTime;


    private void Awake()
    {
        foreach (PlayerPanel panel in panels)
        {
            if (panel == null || panel.content == null) continue;
            panel.baseContentX = panel.content.anchoredPosition.x;
            panel.rowPosition = panel.content.anchoredPosition;
        }

        if (canvasRoot != null) canvasRoot.SetActive(false);
    }

    private void OnEnable()
    {
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
        MinigameInfo.OnPlayerReady += HandlePlayerReady;
        MinigameInfo.OnAllPlayersReady += HandleAllPlayersReady;
        if (fader != null) fader.OnFadeCompleted += HandleFadeCompleted;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
        MinigameInfo.OnPlayerReady -= HandlePlayerReady;
        MinigameInfo.OnAllPlayersReady -= HandleAllPlayersReady;
        if (fader != null) fader.OnFadeCompleted -= HandleFadeCompleted;
    }

    private void Start()
    {
        // シーンの読み込みが遅れて、もう準備確認が始まっていた場合(出す時刻を過ぎていれば、すぐ出る)
        if (MinigameInfo.IsReadyCheck) pendingShow = true;
    }

    private void Update()
    {
        // 全員が同じ瞬間に出す・消すように、サーバーが決めた時刻(サーバー時刻)になるまで待つ
        if (pendingShow && IsDue(MinigameInfo.ReadyUiShowServerTime))
        {
            pendingShow = false;
            Show();
        }
        if (pendingHide && shown && IsDue(MinigameInfo.ReadyUiHideServerTime))
        {
            pendingHide = false;
            Hide();
        }

        if (!shown || hiding) return;

        // イベントを取りこぼしても(後から読み込まれた・通信の順番がずれた)、同期された状態に合わせる
        RefreshPanels(true);

        // 全員そろった合図を取りこぼしたまま、次のフェーズに進んでいたら消す。
        // 表示した直後は値(Phase)がまだ前のフェーズのことがあるので、値が届くまで少し待ってから見る
        if (Time.unscaledTime - shownTime > PhaseSyncGraceSeconds
            && MinigameInfo.IsActive && !MinigameInfo.IsReadyCheck && MinigameInfo.Phase != MinigamePhase.WaitingForPlayers)
        {
            Hide();
        }
    }

    // キャラクターやカメラが動いた後に、ボタンの位置を合わせる
    private void LateUpdate()
    {
        if (!shown || placement != Placement.FollowPlayer) return;
        FollowPlayers();
    }


    // ==================== イベント ====================

    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        if (phase == MinigamePhase.ReadyCheck)
        {
            pendingShow = true; // 出す時刻になったら Update で出す
        }
        else
        {
            pendingShow = false;
            if (shown) Hide(); // 準備確認が終わった(全員そろった合図を取りこぼしたときの保険)
        }
    }

    private void HandlePlayerReady(int slot)
    {
        if (!shown || hiding) return;
        if (slot < 0 || slot >= panels.Length || panels[slot] == null) return;

        // パネルがまだ出ていなければ(人数の同期が遅れたなど)、先に並べ直す
        if (panels[slot].root != null && !panels[slot].root.activeSelf) RefreshPanels(false);
        MarkReady(panels[slot], true);
    }

    private void HandleAllPlayersReady()
    {
        pendingHide = true; // 消し始める時刻になったら Update で消す
    }

    // その時刻(サーバー時刻)になったか。時刻が決まっていなければ(-1)すぐ
    private static bool IsDue(double serverTime)
    {
        return serverTime < 0 || MinigameInfo.ServerTime >= serverTime;
    }

    private void HandleFadeCompleted()
    {
        if (!hiding) return;
        if (canvasRoot != null) canvasRoot.SetActive(false);
        shown = false;
        hiding = false;
    }


    // ==================== 表示 ====================

    private void Show()
    {
        if (shown) return;
        shown = true;
        hiding = false;
        shownTime = Time.unscaledTime;

        if (canvasRoot != null) canvasRoot.SetActive(true);

        foreach (PlayerPanel panel in panels)
        {
            if (panel == null) continue;
            panel.shownReady = false;
            panel.player = null;
            if (panel.textSwitcher != null) panel.textSwitcher.ResetState();
        }

        visibleSlots.Clear();
        nextPlayerSearchTime = 0f;
        RefreshPanels(false); // 開いた時点でもう押していた人は、音・演出なしで「OK!」にする
        if (placement == Placement.FollowPlayer) FollowPlayers();

    }

    private void Hide()
    {
        if (hiding) return;
        hiding = true;

        if (fader != null && canvasRoot != null && canvasRoot.activeInHierarchy)
        {
            fader.Play();
        }
        else
        {
            if (canvasRoot != null) canvasRoot.SetActive(false);
            shown = false;
            hiding = false;
        }
    }

    // いる人のパネルだけを出し、押した人を「OK!」にする
    private void RefreshPanels(bool playEffects)
    {
        // まだ人数が届いていなければ、置いたままにしておく
        if (MinigameInfo.PlayerCount == 0) return;

        List<int> slots = new List<int>();
        for (int slot = 0; slot < panels.Length; slot++)
        {
            PlayerPanel panel = panels[slot];
            if (panel == null || panel.root == null) continue;

            MinigamePlayerState player;
            bool exists = MinigameInfo.TryGetPlayerBySlot(slot, out player);
            if (panel.root.activeSelf != exists) panel.root.SetActive(exists);
            if (!exists) continue;

            slots.Add(slot);
            if (player.IsReady && !panel.shownReady) MarkReady(panel, playEffects);
        }

        // 出ているパネルが変わったときだけ並べ直す
        if (!SameSlots(slots, visibleSlots))
        {
            visibleSlots.Clear();
            visibleSlots.AddRange(slots);
            LayoutRow();
        }
    }

    private void MarkReady(PlayerPanel panel, bool playEffects)
    {
        if (panel.shownReady || panel.root == null || !panel.root.activeInHierarchy) return;
        panel.shownReady = true;

        if (panel.textSwitcher != null) panel.textSwitcher.Play();
        if (!playEffects) return;

        if (panel.punchScale != null) panel.punchScale.Play();
        if (panel.whiteFlash != null) panel.whiteFlash.Play();
        if (!string.IsNullOrEmpty(readySoundId) && SoundManager.Instance != null) SoundManager.Instance.PlaySe(readySoundId);
    }


    // ==================== 位置 ====================

    // いる人だけを、置いたときの間隔のまま真ん中に横並びにする(FollowPlayer でキャラクターが見つからない人もこの位置)
    private void LayoutRow()
    {
        if (placement == Placement.AsPlaced || visibleSlots.Count == 0) return;

        float spacing = GetSpacing();
        for (int i = 0; i < visibleSlots.Count; i++)
        {
            PlayerPanel panel = panels[visibleSlots[i]];
            if (panel.content == null) continue;

            Vector2 position = panel.content.anchoredPosition;
            position.x = (i - (visibleSlots.Count - 1) * 0.5f) * spacing;
            panel.rowPosition = position;
            panel.content.anchoredPosition = position;
        }
    }

    // それぞれのボタンを、その人のキャラクターの上に置く
    private void FollowPlayers()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        // キャラクターは後から出てくることもあるので、見つかっていない人がいれば少しずつ探し直す
        if (Time.unscaledTime >= nextPlayerSearchTime)
        {
            nextPlayerSearchTime = Time.unscaledTime + 0.5f;
            FindPlayers();
        }

        foreach (int slot in visibleSlots)
        {
            PlayerPanel panel = panels[slot];
            if (panel.content == null) continue;

            if (panel.player == null || panel.player.SlotIndex != slot)
            {
                panel.content.anchoredPosition = panel.rowPosition;
                continue;
            }

            Vector3 world = panel.player.transform.position + playerWorldOffset;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) continue; // カメラの後ろにいるときは動かさない

            RectTransform parent = panel.content.parent as RectTransform;
            Canvas canvas = panel.content.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            Vector2 local;
            if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out local))
            {
                panel.content.anchoredPosition = local + screenOffset;
            }
        }
    }

    private void FindPlayers()
    {
        bool missing = false;
        foreach (int slot in visibleSlots)
        {
            PlayerPanel panel = panels[slot];
            if (panel.player == null || panel.player.SlotIndex != slot) { panel.player = null; missing = true; }
        }
        if (!missing) return;

        foreach (PlayerSlot player in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            int slot = player.SlotIndex;
            if (slot >= 0 && slot < panels.Length && panels[slot] != null) panels[slot].player = player;
        }
    }

    // 1P と 2P のパネルを置いた間隔(横並びにするときの間隔に使う)
    private float GetSpacing()
    {
        if (panels.Length >= 2 && panels[0] != null && panels[1] != null && panels[0].content != null && panels[1].content != null)
        {
            float spacing = Mathf.Abs(panels[1].baseContentX - panels[0].baseContentX);
            if (spacing > 0.01f) return spacing;
        }
        return 200f;
    }

    private static bool SameSlots(List<int> a, List<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
