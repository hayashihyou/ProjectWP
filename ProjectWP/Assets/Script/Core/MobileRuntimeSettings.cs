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
        // PCもスマホも60fpsを上限にする
        // PCは何も指定しないと上限なしで回り続け、MPPMで4人分動かすと
        // GPS・CPUを取り合って、遊ぶほどだんだん処理が遅くなるため
        Application.targetFrameRate = 60;

        if (!Application.isMobilePlatform) return;

        // ゲーム中は触ってなくても画面が消えないようにする
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }
}
