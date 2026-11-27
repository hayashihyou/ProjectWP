using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 押したときに「せってい」メニュー(SettingsMenu: 音量設定 / ゲーム終了)を開くボタン。
/// タイトル画面の右上の歯車に付けてある。
///
/// 歯車の画像を使っていても、これが付いているボタンには SoundSettingsOpener(音量画面を直接開く)は自動で付かない。
/// </summary>
[RequireComponent(typeof(Button))]
public class SettingsMenuOpener : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => SettingsMenu.Open(gameObject));
    }
}
