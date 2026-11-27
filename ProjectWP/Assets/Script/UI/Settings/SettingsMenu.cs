using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 歯車ボタンから開く「せってい」メニュー(音量設定 / ゲーム終了 / 閉じる)。
///
/// 開き方: ボタンに SettingsMenuOpener を付けるだけ(タイトル画面の右上の歯車に付けてある)。
///         コードからなら SettingsMenu.Open() でも開ける。
/// 閉じ方: 「閉じる」ボタン / 画面の暗いところをクリック / Esc / パッドのBボタン
///
/// 見た目は Assets/Resources/SettingsMenu.prefab。シーンに置く必要はなく、初めて開いたときに自動で作られ、
/// シーンをまたいでも残る。開いている間は自分のゲーム側の操作は効かなくなる(GameInputLock)。
///
/// 「ゲーム終了」は押すと確認を出し、「はい」で終了する。
/// エディタでは Play を止める。Web版・iPhone ではアプリから終了できないので、ボタン自体を出さない。
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    // Assets/Resources/ 以下のパス(拡張子なし)
    private const string PrefabPath = "SettingsMenu";

    [Tooltip("開いている間だけ表示する部分(暗い背景 + パネル)")]
    [SerializeField] private GameObject _root;

    [Header("メニュー")]
    [Tooltip("メニューのボタンが並んでいるパネル(終了の確認を出している間は隠す)")]
    [SerializeField] private GameObject _menuPanel;
    [SerializeField] private Button _soundButton;
    [SerializeField] private Button _quitButton;
    [SerializeField] private Button _closeButton;
    [Tooltip("パネルの外の暗いところ。押すと閉じる")]
    [SerializeField] private Button _backgroundButton;

    [Header("終了の確認")]
    [Tooltip("「ゲームを終了しますか?」のパネル")]
    [SerializeField] private GameObject _quitConfirmPanel;
    [SerializeField] private Button _quitYesButton;
    [SerializeField] private Button _quitNoButton;

    [Tooltip("シーンに EventSystem が無いときだけ使う予備(普段は OFF)")]
    [SerializeField] private EventSystem _fallbackEventSystem;

    private static SettingsMenu _instance;

    // 閉じたときに選択を戻すボタン(パッドで操作しているときに、どこを選んでいたか分からなくならないように)
    private GameObject _returnSelection;

    /// <summary>今開いているか</summary>
    public static bool IsOpen => _instance != null && _instance._root.activeSelf;

    /// <summary>
    /// メニューを開く。
    /// </summary>
    /// <param name="returnSelection">閉じたときに選択を戻すもの(開いたボタンなど)。省略可</param>
    public static void Open(GameObject returnSelection = null)
    {
        GetOrCreate()?.Show(returnSelection);
    }

    /// <summary>メニューを閉じる</summary>
    public static void Close()
    {
        if (_instance != null) { _instance.Hide(); }
    }

    private static SettingsMenu GetOrCreate()
    {
        if (_instance != null) { return _instance; }

        var prefab = Resources.Load<SettingsMenu>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[SettingsMenu] Resources/{PrefabPath}.prefab が見つかりません。");
            return null;
        }
        _instance = Instantiate(prefab);
        _instance.name = prefab.name;
        DontDestroyOnLoad(_instance.gameObject);
        return _instance;
    }

    private void Awake()
    {
        _soundButton.onClick.AddListener(OpenSoundSettings);
        _quitButton.onClick.AddListener(ShowQuitConfirm);
        _closeButton.onClick.AddListener(Hide);
        _backgroundButton.onClick.AddListener(Hide);
        _quitYesButton.onClick.AddListener(QuitGame);
        _quitNoButton.onClick.AddListener(HideQuitConfirm);

#if UNITY_WEBGL || UNITY_IOS
        // Web版・iPhone はアプリから終了できないので、「ゲーム終了」を出さない
        _quitButton.gameObject.SetActive(false);
#endif

        SetupNavigation(_soundButton, _quitButton, _closeButton);
        SetupNavigation(_quitYesButton, _quitNoButton);
        _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_instance == this) { _instance = null; }
        GameInputLock.Unlock(this);
    }

    // パッド・矢印キーで選ぶものが、このパネルの中だけを上下に行き来するようにする
    // (自動のままだと、後ろの画面のボタンに選択が移ってしまう)。非表示のボタンは飛ばす
    private static void SetupNavigation(params Selectable[] all)
    {
        var items = System.Array.FindAll(all, s => s != null && s.gameObject.activeSelf);
        for (int i = 0; i < items.Length; i++)
        {
            Navigation nav = items[i].navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = items[(i - 1 + items.Length) % items.Length];
            nav.selectOnDown = items[(i + 1) % items.Length];
            nav.selectOnLeft = null;
            nav.selectOnRight = null;
            items[i].navigation = nav;
        }
    }

    private void Update()
    {
        if (!_root.activeSelf) { return; }

        UpdateFallbackEventSystem();

        // Esc かパッドのBボタン(東側のボタン)で、確認中なら確認を閉じる・そうでなければメニューを閉じる
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool back = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        if (!escape && !back) { return; }

        if (_quitConfirmPanel.activeSelf) { HideQuitConfirm(); }
        else { Hide(); }
    }

    private void Show(GameObject returnSelection)
    {
        _returnSelection = returnSelection;

        _root.SetActive(true);
        _menuPanel.SetActive(true);
        _quitConfirmPanel.SetActive(false);
        GameInputLock.Lock(this);
        UpdateFallbackEventSystem();

        Select(_soundButton); // パッドで操作できるように、いちばん上を選んでおく
    }

    private void Hide()
    {
        if (!_root.activeSelf) { return; }
        CloseWithoutRestoringSelection();
        Select(_returnSelection);
    }

    // メニューを閉じるだけ(選択は戻さない)。音量設定へ切り替えるときに使う
    private void CloseWithoutRestoringSelection()
    {
        _root.SetActive(false);
        _fallbackEventSystem.gameObject.SetActive(false);
        GameInputLock.Unlock(this);
    }

    // 「音量設定」: このメニューを閉じて、音量の画面を開く(閉じたら歯車ボタンに選択が戻る)
    private void OpenSoundSettings()
    {
        CloseWithoutRestoringSelection();
        SoundSettingsMenu.Open(_returnSelection);
    }

    // 「ゲーム終了」: いきなり終わらないよう、確認を出す(誤タップ対策)
    private void ShowQuitConfirm()
    {
        _menuPanel.SetActive(false);
        _quitConfirmPanel.SetActive(true);
        Select(_quitNoButton); // うっかり決定しても終了しないよう、「いいえ」を選んでおく
    }

    private void HideQuitConfirm()
    {
        _quitConfirmPanel.SetActive(false);
        _menuPanel.SetActive(true);
        Select(_quitButton);
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        // エディタでは Application.Quit が効かないので、Play を止める
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void Select(Component target)
    {
        Select(target != null ? target.gameObject : null);
    }

    private static void Select(GameObject target)
    {
        if (EventSystem.current != null) { EventSystem.current.SetSelectedGameObject(target); }
    }

    // シーンに EventSystem が無いと、ボタンが反応しない。そのときだけ予備を使う
    private void UpdateFallbackEventSystem()
    {
        bool hasOther = false;
        foreach (EventSystem es in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            if (es != _fallbackEventSystem) { hasOther = true; break; }
        }
        _fallbackEventSystem.gameObject.SetActive(!hasOther);
    }
}
