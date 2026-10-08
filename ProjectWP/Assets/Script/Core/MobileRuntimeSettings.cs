using UnityEngine;


/// <summary>
/// スマホ/タブレットで動かす時だけ必要な、起動時の設定をまとめたクラス
/// シーンには何も置かなくて良い(ゲーム起動時に自動で一回だけ呼ばれる)
/// </summary>
public class MobileRuntimeSettings : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        if(!Application.isMobilePlatform) return;

        // ゲーム中は触ってなくても画面が消えないようにする
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // スマホ/タブレットは何も指定しないと30fpsになってしまうので、pcと同じ60fpsにする
        Application.targetFrameRate = 60;
    }
}
