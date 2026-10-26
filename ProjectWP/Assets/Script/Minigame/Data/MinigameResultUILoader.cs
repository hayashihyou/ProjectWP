using UnityEngine;
using UnityEngine.SceneManagement;


/// <summary>
/// リザルトになったら、共通のリザルトUIシーンを今のシーンに重ねて読み込む(各ミニゲームのシーンに1つ置く)。
/// 
/// ・読みこみは各端末で別々に行う(UI シーンにはネットワークのオブジェクトを置かないこと)
/// ・背景はミニゲームのシーンがそのまま使われる(MinigameResultBackground)
/// ・「もう一度」「ミニゲーム選択へ」で次のシーンに移ると、UIシーンも一緒に自動で片付く
/// </summary>
public class MinigameResultUILoader : MonoBehaviour
{
    [Tooltip("重ねて読み込むリザルトUIのシーン名")]
    [SerializeField] private string resultSceneName = SceneNames.MinigameResult;


    private void OnEnable()
    {
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
    }


    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
    }


    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        if (phase != MinigamePhase.Result) return;

        // すでに読み込んでいれば何もしない
        if (SceneManager.GetSceneByName(resultSceneName).isLoaded) return;

        if (!Application.CanStreamedLevelBeLoaded(resultSceneName)) return;

        if(!Application.CanStreamedLevelBeLoaded(resultSceneName))
        {
            Debug.LogWarning($"[ResultUILoader]シーン「{resultSceneName}」がBuildSettingsにありません。File > BuildProfilesのSceneListに追加してください。");
            return;
        }

        SceneManager.LoadSceneAsync(resultSceneName, LoadSceneMode.Additive);
    }
}
