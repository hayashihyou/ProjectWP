using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YourGame.SceneManagement
{
    /// <summary>
    /// 一定時間後に指定シーンへ遷移させるビヘイビア。
    /// フェード有無はチェックボックス、時間は数値として
    /// コードを触らずInspectorから調整できる。
    ///
    /// SceneLoadTester の後継。動作確認だけでなく、
    /// 「タイトル画面で数秒待って次のシーンへ」のような
    /// 実運用の演出トリガーとしてもそのまま使える。
    /// </summary>
    public class SceneTransitionTrigger : MonoBehaviour
    {
        [Header("遷移先")]
        [Tooltip("読み込むシーン名（Build Settingsに追加されていること）")]
        [SerializeField] private string _targetSceneName = "Title";

        [Header("タイミング")]
        [Tooltip("このオブジェクトのStart()からこの秒数が経過したら遷移を開始する")]
        [SerializeField] private float _delaySeconds = 3f;

        [Tooltip("実際のロードが一瞬で終わっても、最低このくらいはロード扱いにしたい場合の秒数。不要なら0")]
        [SerializeField] private float _minimumLoadDuration = 0f;

        [Header("フェード")]
        [Tooltip("ONにすると フェードアウト → ロード → フェードイン の演出付きで遷移する")]
        [SerializeField] private bool _useFade = true;

        [Tooltip("フェードアウトの秒数（SceneLoader側のデフォルトを使いたい場合は-1）")]
        [SerializeField] private float _fadeOutDuration = 0.3f;

        [Tooltip("フェードインの秒数（SceneLoader側のデフォルトを使いたい場合は-1）")]
        [SerializeField] private float _fadeInDuration = 0.3f;

        [Header("ローディング表示")]
        [Tooltip("ONにするとSceneLoaderに設定したLoading Indicatorをロード中だけ表示する。OFFならLoading Indicatorが未設定/未完成でも一切触らない")]
        [SerializeField] private bool _useLoadingIndicator = false;

        private async UniTaskVoid Start()
        {
            Debug.Log($"[SceneTransitionTrigger] {_targetSceneName} へ{_delaySeconds}秒後に切り替えます...");

            await UniTask.Delay(
                TimeSpan.FromSeconds(_delaySeconds),
                cancellationToken: destroyCancellationToken);

            Debug.Log("[SceneTransitionTrigger] シーン切り替えを開始します");

            await SceneLoader.Instance.LoadSceneAsync(
                _targetSceneName,
                useFade: _useFade,
                useLoadingIndicator: _useLoadingIndicator,
                fadeOutDuration: _fadeOutDuration,
                fadeInDuration: _fadeInDuration,
                minimumDuration: _minimumLoadDuration,
                ct: destroyCancellationToken);

            Debug.Log("[SceneTransitionTrigger] シーン切り替えが完了しました");
        }
    }
}