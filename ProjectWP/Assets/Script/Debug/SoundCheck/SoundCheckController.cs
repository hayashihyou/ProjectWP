using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 【開発用】サウンド確認シーン(Assets/Scenes/SoundCheck.unity)の本体。
///
///   ・右の一覧に、SoundLibrary に登録されている音を「使用場面」ごとに並べる(手で書き足す必要はない)
///   ・一覧から選ぶと、詳細パネルにファイルの場所・音量などが出る
///   ・同時に鳴らせるのは BGM 1つ + SE 1つ まで(BGM と SE のバランスを確認できる)
///   ・同じ種類の別の音を鳴らすと「今の音がフェードアウトして完全に消える → 新しい音がフェードインする」
///
/// 詳細パネルの「音量」だけは SoundLibrary.asset に直接書き込む(Play を止めても残る)。
/// フェードイン / フェードアウト / ループのスイッチは「ここで試しに鳴らすとき」だけの設定で、ゲームには影響しない。
/// ゲーム本番での鳴らし方は、SoundLibrary.asset の Fade In Seconds / Fade Out Seconds / Loop で決める。
/// </summary>
public class SoundCheckController : MonoBehaviour
{
    // SoundManager と同じ SoundLibrary を読む
    private const string LibraryResourcePath = "SoundLibrary";

    // 「使用場面」が未入力の音をまとめる見出し
    private const string NoSceneLabel = "(使用場面 未入力)";

    // スイッチの秒数の初期値(SoundLibrary の値が 0 のとき)
    private const float DefaultTestFadeSeconds = 0.5f;

    // 「すべて停止」のフェードアウト秒数
    private const float StopAllFadeSeconds = 0.5f;

    [Header("一覧")]
    [SerializeField] private Transform _listContent;
    [Tooltip("音1つ分の行の見本(非表示にしておく)。これを複製して一覧を作る")]
    [SerializeField] private SoundCheckRow _rowTemplate;
    [Tooltip("使用場面の見出しの見本(非表示にしておく)")]
    [SerializeField] private TMP_Text _headerTemplate;

    [Header("詳細パネル")]
    [Tooltip("音を選ぶまで隠しておく部分")]
    [SerializeField] private GameObject _detailRoot;
    [Tooltip("音を選ぶまで出しておく案内")]
    [SerializeField] private GameObject _emptyMessage;
    [SerializeField] private TMP_Text _sceneText;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _pathText;
    [SerializeField] private TMP_Text _infoText;
    [SerializeField] private Slider _volumeSlider;
    [SerializeField] private TMP_InputField _volumeInput;
    [SerializeField] private Toggle _fadeInToggle;
    [SerializeField] private TMP_InputField _fadeInSecondsInput;
    [SerializeField] private Toggle _fadeOutToggle;
    [SerializeField] private TMP_InputField _fadeOutSecondsInput;
    [SerializeField] private Toggle _loopToggle;
    [SerializeField] private Button _playButton;
    [SerializeField] private TMP_Text _playButtonLabel;
    [SerializeField] private Image _playButtonImage;

    [Header("全体")]
    [SerializeField] private Button _stopAllButton;
    [Tooltip("今鳴っている BGM の表示")]
    [SerializeField] private TMP_Text _nowBgmText;
    [Tooltip("今鳴っている SE の表示")]
    [SerializeField] private TMP_Text _nowSeText;

    [Header("Playボタンの色")]
    [SerializeField] private Color _playColor = new Color(0.25f, 0.88f, 0.82f);
    [SerializeField] private Color _stopColor = new Color(1f, 0.3f, 0.55f);
    [SerializeField] private Color _waitColor = new Color(0.55f, 0.58f, 0.7f);

    /// <summary>
    /// 種類(BGM / SE)ごとの「1つだけ鳴らせる枠」。
    /// 今鳴っている音と、前の音が消えるのを待っている「次の音」を持つ。
    /// </summary>
    private class Slot
    {
        public SoundEntry Current;   // 今鳴っている(またはフェードアウト中の)音
        public SoundHandle Handle;   // SE を止めるためのハンドル(BGM は SoundManager に聞く)
        public SoundEntry Next;      // 前の音が消えたら鳴らす音
        public Coroutine Switching;  // 切り替え中の処理

        // 止めた(フェードアウト中)かどうか。SE のハンドルは消えきるまで IsPlaying が true のままなので自分で覚えておく
        public bool Stopping;
    }

    private readonly Slot _bgm = new Slot();
    private readonly Slot _se = new Slot();

    private SoundLibrary _library;
    private readonly List<SoundCheckRow> _rows = new List<SoundCheckRow>();
    private SoundCheckRow _selected;

    // 詳細パネルの値をコードから入れている最中は、変更イベントで SoundLibrary を書き換えないようにする
    private bool _isRefreshingDetail;

    private static SoundCheckController _instance;

    /// <summary>何か音が鳴っているか(レコード盤を回すかどうかに使う)</summary>
    public static bool IsAnythingPlaying
    {
        get
        {
            SoundManager sound = SoundManager.Instance;
            return sound.CurrentBgmId != null || sound.ActiveSeCount > 0;
        }
    }

    /// <summary>レコード盤のラベルに出す名前(BGM が鳴っていれば BGM、なければ SE)</summary>
    public static string NowPlayingName
    {
        get
        {
            if (_instance == null) { return ""; }
            if (_instance.IsPlaying(_instance._bgm)) { return DisplayName(_instance._bgm.Current); }
            if (_instance.IsPlaying(_instance._se)) { return DisplayName(_instance._se.Current); }
            return "";
        }
    }

    private void Awake()
    {
        _instance = this;
    }

    private void Start()
    {
        _library = Resources.Load<SoundLibrary>(LibraryResourcePath);
        if (_library == null)
        {
            Debug.LogError($"[SoundCheck] Resources/{LibraryResourcePath}.asset が見つかりません。");
            return;
        }

        _rowTemplate.gameObject.SetActive(false);
        _headerTemplate.gameObject.SetActive(false);
        BuildList();

        _volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
        _volumeInput.onEndEdit.AddListener(OnVolumeInputEdited);
        _playButton.onClick.AddListener(OnPlayButton);
        _stopAllButton.onClick.AddListener(StopAll);

        ShowDetail(null);
    }

    private void Update()
    {
        // 一覧の色と Play/Stop の表示を、今鳴っているかどうかに合わせる
        foreach (SoundCheckRow row in _rows)
        {
            Slot slot = SlotOf(row.Entry);
            row.SetState(row == _selected, IsPlaying(slot) && slot.Current == row.Entry, slot.Next == row.Entry);
        }

        if (_selected != null)
        {
            Slot slot = SlotOf(_selected.Entry);
            if (slot.Next == _selected.Entry)
            {
                _playButtonLabel.text = "WAITING…";
                _playButtonImage.color = _waitColor;
            }
            else if (IsPlaying(slot) && slot.Current == _selected.Entry)
            {
                _playButtonLabel.text = "■  STOP";
                _playButtonImage.color = _stopColor;
            }
            else
            {
                // ▶ は PLAY の文字のフォント(源暎)に入っていないので、その文字だけ YuGothic で出す
                _playButtonLabel.text = "<font=\"YuGothic Regular SDF\">▶</font>  PLAY";
                _playButtonImage.color = _playColor;
            }
        }

        _nowBgmText.text = NowText(_bgm);
        _nowSeText.text = NowText(_se);
    }

    private string NowText(Slot slot)
    {
        if (slot.Next != null) { return $"{DisplayName(slot.Next)}  <alpha=#88>(前の音が消えるのを待っています)"; }
        if (IsPlaying(slot)) { return DisplayName(slot.Current); }
        return "<alpha=#55>—";
    }

    // ==================== 一覧 ====================

    // SoundLibrary の音を「使用場面」ごとにまとめて並べる(場面の順番は SoundLibrary で最初に出てきた順)
    private void BuildList()
    {
        var groupOrder = new List<string>();
        var groups = new Dictionary<string, List<SoundEntry>>();

        foreach (SoundEntry entry in _library.Entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) { continue; }

            string scene = string.IsNullOrEmpty(entry.sceneLabel) ? NoSceneLabel : entry.sceneLabel;
            if (!groups.TryGetValue(scene, out List<SoundEntry> list))
            {
                list = new List<SoundEntry>();
                groups.Add(scene, list);
                groupOrder.Add(scene);
            }
            list.Add(entry);
        }

        foreach (string scene in groupOrder)
        {
            TMP_Text header = Instantiate(_headerTemplate, _listContent);
            header.text = scene;
            header.gameObject.SetActive(true);

            foreach (SoundEntry entry in groups[scene])
            {
                SoundCheckRow row = Instantiate(_rowTemplate, _listContent);
                row.Setup(entry, Select);
                row.gameObject.SetActive(true);
                _rows.Add(row);
            }
        }
    }

    private void Select(SoundCheckRow row)
    {
        _selected = row;
        ShowDetail(row.Entry);
    }

    // ==================== 詳細パネル ====================

    private void ShowDetail(SoundEntry entry)
    {
        _detailRoot.SetActive(entry != null);
        _emptyMessage.SetActive(entry == null);
        if (entry == null) { return; }

        _isRefreshingDetail = true;

        _sceneText.text = string.IsNullOrEmpty(entry.sceneLabel) ? NoSceneLabel : entry.sceneLabel;
        _titleText.text = DisplayName(entry);
        _pathText.text = GetClipPath(entry.clip);

        string type = entry.category == SoundCategory.Bgm ? "BGM" : "SE";
        string length = entry.clip != null ? FormatSeconds(entry.clip.length) : "-";
        _infoText.text = $"{type}   ·   {length}   ·   {entry.id}";

        SetVolumeDisplay(entry.volume);

        // フェードは最初から ON。秒数はゲーム本番の設定(SoundLibrary の値)があればそれ、なければ初期値
        SetFadeDisplay(_fadeInToggle, _fadeInSecondsInput, entry.fadeInSeconds);
        SetFadeDisplay(_fadeOutToggle, _fadeOutSecondsInput, entry.fadeOutSeconds);
        _loopToggle.isOn = entry.loop;

        _isRefreshingDetail = false;
    }

    private static void SetFadeDisplay(Toggle toggle, TMP_InputField input, float seconds)
    {
        toggle.isOn = true;
        input.text = FormatNumber(seconds > 0f ? seconds : DefaultTestFadeSeconds);
    }

    private void SetVolumeDisplay(float volume)
    {
        _volumeSlider.SetValueWithoutNotify(Mathf.Round(volume * 100f));
        _volumeInput.SetTextWithoutNotify(Mathf.RoundToInt(volume * 100f).ToString());
    }

    // ---------- 音量(SoundLibrary に書き込む) ----------

    private void OnVolumeSliderChanged(float value)
    {
        if (_isRefreshingDetail || _selected == null) { return; }
        ApplyVolume(value / 100f);
    }

    private void OnVolumeInputEdited(string text)
    {
        if (_isRefreshingDetail || _selected == null) { return; }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            ApplyVolume(Mathf.Clamp(value, 0f, 100f) / 100f);
        }
        else
        {
            SetVolumeDisplay(_selected.Entry.volume); // 数字でなければ元に戻す
        }
    }

    // 鳴っている音にもすぐ反映される(SoundVoice が毎フレーム Entry.volume を読んでいるため)
    private void ApplyVolume(float volume)
    {
        _selected.Entry.volume = volume;
        SetVolumeDisplay(volume);
        MarkLibraryDirty();
    }

    // ==================== 再生 ====================

    private void OnPlayButton()
    {
        if (_selected == null) { return; }
        SoundEntry entry = _selected.Entry;
        Slot slot = SlotOf(entry);
        float fadeOut = ReadFadeSeconds(_fadeOutToggle, _fadeOutSecondsInput);

        // 待っている音をもう一度押したら取り消す
        if (slot.Next == entry)
        {
            CancelSwitch(slot);
            return;
        }

        // 鳴っている音を押したら止める
        if (IsPlaying(slot) && slot.Current == entry)
        {
            StopSlot(slot, fadeOut);
            return;
        }

        // それ以外は「今の音を消してから鳴らす」
        CancelSwitch(slot);
        slot.Next = entry;
        slot.Switching = StartCoroutine(SwitchRoutine(slot, entry, fadeOut,
            ReadFadeSeconds(_fadeInToggle, _fadeInSecondsInput), _loopToggle.isOn));
    }

    /// <summary>今の音をフェードアウトさせ、完全に消えてから次の音をフェードインさせる</summary>
    private IEnumerator SwitchRoutine(Slot slot, SoundEntry next, float fadeOut, float fadeIn, bool loop)
    {
        if (IsSounding(slot))
        {
            StopSlot(slot, fadeOut);
            yield return new WaitUntil(() => !IsSounding(slot));
        }

        SoundManager sound = SoundManager.Instance;
        if (next.category == SoundCategory.Bgm)
        {
            sound.PlayBgm(next.id, fadeIn, restartIfSame: true, loop: loop);
        }
        else
        {
            slot.Handle = sound.PlaySe(next.id, loop: loop, fadeInSeconds: fadeIn);
        }

        slot.Current = next;
        slot.Stopping = false;
        slot.Next = null;
        slot.Switching = null;
    }

    private void StopSlot(Slot slot, float fadeSeconds)
    {
        slot.Stopping = true;
        if (slot == _bgm) { SoundManager.Instance.StopBgm(fadeSeconds); }
        else { slot.Handle.Stop(fadeSeconds); }
    }

    private void CancelSwitch(Slot slot)
    {
        if (slot.Switching != null) { StopCoroutine(slot.Switching); }
        slot.Switching = null;
        slot.Next = null;
    }

    private void StopAll()
    {
        CancelSwitch(_bgm);
        CancelSwitch(_se);
        StopSlot(_bgm, StopAllFadeSeconds);
        StopSlot(_se, StopAllFadeSeconds);
    }

    private Slot SlotOf(SoundEntry entry) => entry.category == SoundCategory.Bgm ? _bgm : _se;

    // 鳴っている(フェードアウト中は含まない) … 表示用
    private bool IsPlaying(Slot slot)
    {
        if (slot.Current == null) { return false; }
        if (slot == _bgm) { return SoundManager.Instance.CurrentBgmId == slot.Current.id; }
        return slot.Handle.IsPlaying && !slot.Stopping;
    }

    // 少しでも音が出ている(フェードアウト中も含む) … 「完全に消えたか」の判定用
    private bool IsSounding(Slot slot)
    {
        if (slot == _bgm) { return SoundManager.Instance.IsBgmSounding; }
        return slot.Handle.IsPlaying;
    }

    // スイッチが OFF なら 0(フェードなし)。ON なら入力された秒数
    private static float ReadFadeSeconds(Toggle toggle, TMP_InputField input)
    {
        if (!toggle.isOn) { return 0f; }
        return float.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
            ? Mathf.Max(0f, seconds)
            : DefaultTestFadeSeconds;
    }

    // ==================== 補助 ====================

    private static string DisplayName(SoundEntry entry)
    {
        return string.IsNullOrEmpty(entry.usageLabel) ? entry.id : entry.usageLabel;
    }

    private static string GetClipPath(AudioClip clip)
    {
        if (clip == null) { return "(未設定)"; }
#if UNITY_EDITOR
        return AssetDatabase.GetAssetPath(clip);
#else
        return clip.name;
#endif
    }

    private static string FormatSeconds(float seconds)
    {
        int minutes = (int)(seconds / 60f);
        return $"{minutes}:{seconds - minutes * 60f:00.0}";
    }

    private static string FormatNumber(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // エディタで Play 中に ScriptableObject を書き換えると、Play を止めても値が残る。
    // ただし「変更あり」の印を付けないとファイルに保存されないことがあるので、ここで付けておく。
    private void MarkLibraryDirty()
    {
#if UNITY_EDITOR
        EditorUtility.SetDirty(_library);
#endif
    }

    private void OnDestroy()
    {
        if (_instance == this) { _instance = null; }
#if UNITY_EDITOR
        if (_library != null) { AssetDatabase.SaveAssetIfDirty(_library); }
#endif
    }
}
