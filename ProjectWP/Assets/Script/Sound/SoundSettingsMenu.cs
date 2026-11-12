using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// サウンド設定画面(マスター・BGM・SE の音量)。
///
/// 開き方: ボタンに SoundSettingsOpener を付けるだけ(ステージ選択画面の右上の歯車に付けてある)。
///         コードからなら SoundSettingsMenu.Open() でも開ける。
/// 閉じ方: 「閉じる」ボタン / 画面の暗いところをクリック / Esc / パッドのBボタン
///
/// 見た目は Assets/Resources/SoundSettingsMenu.prefab。シーンに置く必要はなく、初めて開いたときに自動で作られ、
/// シーンをまたいでも残る。画面を開いてもゲームの進行は止まらない(オンラインなので)が、
/// 開いている間は自分のゲーム側の操作(移動・ミニゲームの操作・準備OK など)は効かなくなる(GameInputLock)。
/// 音量は SoundManager.SetVolume で設定され、自動で保存される(次に起動したときも同じ音量)。
/// </summary>
public class SoundSettingsMenu : MonoBehaviour
{
    // Assets/Resources/ 以下のパス(拡張子なし)
    private const string PrefabPath = "SoundSettingsMenu";

    /// <summary>スライダー1本分(どの音量を動かすか・スライダー・数字の表示)</summary>
    [Serializable]
    private class VolumeSlider
    {
        public VolumeType type;
        public Slider slider;
        public TMP_Text valueText;
    }

    [Tooltip("開いている間だけ表示する部分(暗い背景 + パネル)")]
    [SerializeField] private GameObject _root;

    [SerializeField] private VolumeSlider[] _sliders;

    [SerializeField] private Button _closeButton;
    [Tooltip("パネルの外の暗いところ。押すと閉じる")]
    [SerializeField] private Button _backgroundButton;

    [Tooltip("シーンに EventSystem が無いときだけ使う予備(普段は OFF)")]
    [SerializeField] private EventSystem _fallbackEventSystem;

    [Tooltip("SE のスライダーを離したときに鳴らす音(SE の大きさを確かめるため)。空欄なら鳴らさない")]
    [SerializeField] private string _sePreviewId = ButtonSound.DefaultDecideId;

    private static SoundSettingsMenu _instance;

    // 閉じたときに選択を戻すボタン(パッドで操作しているときに、どこを選んでいたか分からなくならないように)
    private GameObject _returnSelection;

    /// <summary>今開いているか</summary>
    public static bool IsOpen => _instance != null && _instance._root.activeSelf;

    /// <summary>
    /// 設定画面を開く。
    /// </summary>
    /// <param name="returnSelection">閉じたときに選択を戻すもの(開いたボタンなど)。省略可</param>
    public static void Open(GameObject returnSelection = null)
    {
        GetOrCreate()?.Show(returnSelection);
    }

    /// <summary>設定画面を閉じる</summary>
    public static void Close()
    {
        if (_instance != null) { _instance.Hide(); }
    }

    private static SoundSettingsMenu GetOrCreate()
    {
        if (_instance != null) { return _instance; }

        var prefab = Resources.Load<SoundSettingsMenu>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[SoundSettingsMenu] Resources/{PrefabPath}.prefab が見つかりません。");
            return null;
        }
        _instance = Instantiate(prefab);
        _instance.name = prefab.name;
        DontDestroyOnLoad(_instance.gameObject);
        return _instance;
    }

    private void Awake()
    {
        foreach (VolumeSlider v in _sliders)
        {
            VolumeType type = v.type; // ラムダの中で使うのでコピーしておく
            TMP_Text valueText = v.valueText;
            v.slider.onValueChanged.AddListener(value =>
            {
                SoundManager.Instance.SetVolume(type, value / 100f);
                valueText.text = Mathf.RoundToInt(value).ToString();
            });

            if (type == VolumeType.Se) { AddSePreview(v.slider); }
        }

        _closeButton.onClick.AddListener(Hide);
        _backgroundButton.onClick.AddListener(Hide);

        SetupNavigation();
        _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_instance == this) { _instance = null; }
        GameInputLock.Unlock(this);
    }

    // パッド・矢印キーで選ぶものが、この画面の中(スライダー → 閉じるボタン)だけを行き来するようにする
    // (自動のままだと、後ろの画面のボタンに選択が移ってしまう)
    private void SetupNavigation()
    {
        var items = new Selectable[_sliders.Length + 1];
        for (int i = 0; i < _sliders.Length; i++) { items[i] = _sliders[i].slider; }
        items[_sliders.Length] = _closeButton;

        for (int i = 0; i < items.Length; i++)
        {
            Navigation nav = items[i].navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = items[(i - 1 + items.Length) % items.Length];
            nav.selectOnDown = items[(i + 1) % items.Length];
            nav.selectOnLeft = null;  // スライダーは左右で値を変えるので、左右では移動しない
            nav.selectOnRight = null;
            items[i].navigation = nav;
        }
    }

    private void Update()
    {
        if (!_root.activeSelf) { return; }

        UpdateFallbackEventSystem();

        // Esc かパッドのBボタン(東側のボタン)で閉じる
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool back = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        if (escape || back) { Hide(); }
    }

    private void Show(GameObject returnSelection)
    {
        _returnSelection = returnSelection;

        // 今の音量をスライダーに出す(変更イベントは呼ばない)
        foreach (VolumeSlider v in _sliders)
        {
            float value = Mathf.Round(SoundManager.Instance.GetVolume(v.type) * 100f);
            v.slider.SetValueWithoutNotify(value);
            v.valueText.text = Mathf.RoundToInt(value).ToString();
        }

        _root.SetActive(true);
        GameInputLock.Lock(this);
        UpdateFallbackEventSystem();

        // パッドで操作できるように、いちばん上のスライダーを選んでおく
        if (EventSystem.current != null && _sliders.Length > 0)
        {
            EventSystem.current.SetSelectedGameObject(_sliders[0].slider.gameObject);
        }
    }

    private void Hide()
    {
        if (!_root.activeSelf) { return; }
        _root.SetActive(false);
        _fallbackEventSystem.gameObject.SetActive(false);
        GameInputLock.Unlock(this);

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(_returnSelection);
        }
    }

    // シーンに EventSystem が無いと、ボタンもスライダーも反応しない。そのときだけ予備を使う
    private void UpdateFallbackEventSystem()
    {
        bool hasOther = false;
        foreach (EventSystem es in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            if (es != _fallbackEventSystem) { hasOther = true; break; }
        }
        _fallbackEventSystem.gameObject.SetActive(!hasOther);
    }

    // SE のスライダーから指を離したときに、SE を1回鳴らす(動かしている間ずっと鳴るとうるさいので)
    private void AddSePreview(Slider slider)
    {
        if (string.IsNullOrEmpty(_sePreviewId)) { return; }

        var trigger = slider.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entry.callback.AddListener(_ => SoundManager.Instance.PlaySe(_sePreviewId));
        trigger.triggers.Add(entry);
    }
}
