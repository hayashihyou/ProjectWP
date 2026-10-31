using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ボタンを押したときの音。
///   押せるボタン   → 決定音(クリック・タップ・パッドのAボタン/Enter)
///   押せないボタン → 押せない音(クリック・タップしたとき。パッドでは押せないボタンは選べないので鳴らない)
///
/// シーンを読み込むたびに、そのシーンの全部の Button に自動で付けるので、ボタンを作る人は何もしなくてよい。
/// 違う音にしたいボタン・鳴らしたくないボタンだけ、自分でこのコンポーネントを付けて Inspector で設定する
/// (自分で付けたものは自動で付け直されない。IDを空欄にするとその音は鳴らない)。
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonSound : MonoBehaviour, IPointerClickHandler
{
    public const string DefaultDecideId = "Common_Decide";
    public const string DefaultDisabledId = "Common_Disabled";

    [Tooltip("押したときの音(SoundLibrary のID)。空欄なら鳴らさない")]
    [SerializeField] private string decideId = DefaultDecideId;

    [Tooltip("押せない状態で押したときの音。空欄なら鳴らさない")]
    [SerializeField] private string disabledId = DefaultDisabledId;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        // onClick は押せるときにしか呼ばれない(クリックでもパッドの決定でも呼ばれる)
        button.onClick.AddListener(PlayDecide);
    }

    private void OnDestroy()
    {
        if (button != null) { button.onClick.RemoveListener(PlayDecide); }
    }

    // クリックは押せない状態でも届くので、押せないときだけここで鳴らす
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) { return; }
        if (button.IsInteractable()) { return; }
        Play(disabledId);
    }

    private void PlayDecide() => Play(decideId);

    private static void Play(string id)
    {
        if (!string.IsNullOrEmpty(id)) { SoundManager.Instance.PlaySe(id); }
    }


    // ==================== シーンのボタンに自動で付ける ====================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= AttachToSceneButtons; // ドメインリロードなしで Play し直したときの二重登録を防ぐ
        SceneManager.sceneLoaded += AttachToSceneButtons;
    }

    // 読み込んだシーンの全部の Button(非表示のものも含む)に付ける
    private static void AttachToSceneButtons(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Button b in root.GetComponentsInChildren<Button>(true))
            {
                if (b.GetComponent<ButtonSound>() == null) { b.gameObject.AddComponent<ButtonSound>(); }
            }
        }
    }
}
