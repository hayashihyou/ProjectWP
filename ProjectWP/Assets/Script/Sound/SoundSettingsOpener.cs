using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 押したときにサウンド設定画面(SoundSettingsMenu)を開くボタン。
///
/// 歯車アイコン(Assets/UI/StageSelect/SettingsIcon.png)を使っているボタンには、
/// シーンを読み込むたびに自動で付くので、ボタンを作る人は何もしなくてよい。
/// 歯車以外の見た目のボタンから開きたいときだけ、このコンポーネントを自分で付ける。
/// </summary>
[RequireComponent(typeof(Button))]
public class SoundSettingsOpener : MonoBehaviour
{
    // この画像を使っているボタンに自動で付ける
    private const string GearSpriteName = "SettingsIcon";

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => SoundSettingsMenu.Open(gameObject));
    }


    // ==================== 歯車ボタンに自動で付ける ====================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= AttachToGearButtons; // ドメインリロードなしで Play し直したときの二重登録を防ぐ
        SceneManager.sceneLoaded += AttachToGearButtons;
    }

    // 読み込んだシーンの全部の Button(非表示のものも含む)のうち、歯車の画像を使っているものに付ける
    private static void AttachToGearButtons(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.GetComponent<SoundSettingsOpener>() != null) { continue; }
                if (UsesGearSprite(button)) { button.gameObject.AddComponent<SoundSettingsOpener>(); }
            }
        }
    }

    // ボタン自身か、その子の画像が歯車かどうか(歯車を子オブジェクトに置いているボタンもあるため)
    private static bool UsesGearSprite(Button button)
    {
        foreach (Image image in button.GetComponentsInChildren<Image>(true))
        {
            if (image.sprite != null && image.sprite.name == GearSpriteName) { return true; }
        }
        return false;
    }
}
