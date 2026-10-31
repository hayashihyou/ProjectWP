using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// シーンを読み込んだら、そのシーンのBGMを流す。シーンには何も置かなくてよい。
///
/// ・同じBGMのシーンに移ったときは、曲が途切れず続きから流れる(例: 待機所 → ステージ選択)
/// ・違うBGMのシーンに移ったときは、ローディングに入った時点で今の曲をフェードアウトさせ、
///   次のシーンを読み込み終わったら新しい曲をフェードインさせる
/// ・下の表にないシーン(テスト用のシーンなど)では、BGMを変えない
///
/// シーンやBGMを追加したら、下の表に1行足す。
/// </summary>
public static class SceneBgm
{
    // シーン名 → BGMのID(SoundLibrary のID)
    private static readonly Dictionary<string, string> BgmByScene = new Dictionary<string, string>
    {
        { SceneNames.Title, "BGM_Title" },
        { SceneNames.PlayerJoin, "BGM_Lobby" },
        { SceneNames.StageSelect, "BGM_Lobby" },
        { SceneNames.Stopwatch, "BGM_Stopwatch" },
        { SceneNames.MinigameResult, "BGM_Result" }, // リザルトUIはミニゲームのシーンに重ねて読み込まれる
    };

    // 新しいシーンの曲をフェードインさせる秒数
    private const float FadeSeconds = 0.8f;

    // ローディングに入ったときに、今の曲をフェードアウトさせる秒数
    private const float LoadingFadeOutSeconds = 1.0f;

    /// <summary>
    /// シーンの読み込みが始まった(ローディングに入った)ときに呼ぶ。
    /// 次のシーンの曲が今の曲と違えば、今の曲をフェードアウトさせる(同じ曲・表にないシーンなら何もしない)。
    /// SceneLoader(接続前・切断後の遷移)と NetworkSceneTransition(全員そろっての遷移)から呼ばれる。
    /// </summary>
    public static void NotifyLoadStarted(string nextSceneName)
    {
        if (!BgmByScene.TryGetValue(nextSceneName, out string nextBgmId)) { return; }

        SoundManager sound = SoundManager.Instance;
        if (sound.CurrentBgmId == null || sound.CurrentBgmId == nextBgmId) { return; }

        sound.StopBgm(LoadingFadeOutSeconds);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded; // ドメインリロードなしで Play し直したときの二重登録を防ぐ
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (BgmByScene.TryGetValue(scene.name, out string bgmId))
        {
            SoundManager.Instance.PlayBgm(bgmId, FadeSeconds);
        }
    }
}
