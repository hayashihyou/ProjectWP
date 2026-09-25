using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YourGame.SceneManagement
{
    /// <summary>
    /// シーン遷移を一元管理するクラス。
    /// フェードアウト／フェードインの実処理もこのクラスが持つ。
    ///
    /// このコンポーネントは DontDestroyOnLoad で永続化されるため、
    /// フェード用のCanvas（黒画像＋CanvasGroup）もここに子オブジェクトとして
    /// ぶら下げておけば、どのシーンから呼んでも同じフェードが使い回せる。
    ///
    /// 呼び出し側（SceneTransitionTrigger等）は
    ///   ・フェードを使うかどうか（bool）
    ///   ・フェードや遅延の秒数（float）
    /// だけを渡せばよい。フェードの中身（Lerpやalpha操作）はここに隠蔽する。
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        private static SceneLoader _instance;

        /// <summary>
        /// シングルトンインスタンス。
        /// シーン内に配置済みならそれを使い、なければ自動生成する。
        /// これにより「SceneLoaderが置かれた起動用シーンを経由せず、
        /// 単体のシーンをエディタで直接Playした」場合でも
        /// NullReferenceExceptionにならない。
        /// ただし自動生成された場合はフェード用CanvasGroupが未設定のため、
        /// フェードは自動的にスキップされる（Resourcesフォルダにプレハブを
        /// 置いておけばフェード付きで自動生成できる。下記コメント参照）。
        /// </summary>
        public static SceneLoader Instance
        {
            get
            {
                if (_instance != null) return _instance;

                // シーン内に既に置かれていないか探す
                _instance = FindObjectOfType<SceneLoader>();
                if (_instance != null)
                {
                    DontDestroyOnLoad(_instance.gameObject);
                    return _instance;
                }

                // Resources/SceneLoader.prefab があればそれを使う
                // （CanvasGroupまで設定済みのプレハブを用意しておくとフェードも自動で有効になる）
                var prefab = Resources.Load<SceneLoader>("SceneLoader");
                if (prefab != null)
                {
                    _instance = Instantiate(prefab);
                    _instance.name = "SceneLoader";
                }
                else
                {
                    Debug.LogWarning(
                        "[SceneLoader] シーン内にSceneLoaderが見つからず、Resources/SceneLoader.prefab も" +
                        "見つからなかったため、フェードなしの簡易インスタンスを自動生成しました。" +
                        "フェードを使いたい場合はブートストラップシーンにSceneLoaderを配置するか、" +
                        "Resourcesフォルダに設定済みのSceneLoaderプレハブを置いてください。");
                    var go = new GameObject("SceneLoader (Auto Generated)");
                    _instance = go.AddComponent<SceneLoader>();
                }

                DontDestroyOnLoad(_instance.gameObject);
                return _instance;
            }
        }

        [Header("フェード設定")]
        [Tooltip("画面全体を覆う黒画像等のCanvasGroup。未設定でもエラーにはならず、フェードだけスキップされる")]
        [SerializeField] private CanvasGroup _fadeCanvasGroup;

        [Tooltip("呼び出し側でフェード秒数を指定しなかった場合（-1指定）に使うデフォルト値")]
        [SerializeField] private float _defaultFadeOutDuration = 0.3f;
        [SerializeField] private float _defaultFadeInDuration = 0.3f;

        [Header("ローディング表示")]
        [Tooltip("フェードアウト完了後〜フェードイン開始前の間だけ表示するUI（Now Loadingのテキストやくるくる回るアイコンなど）。任意。FadeCanvasの子にしておくと自動でDontDestroyOnLoad対象になる")]
        [SerializeField] private GameObject _loadingIndicator;

        /// <summary>現在ロード処理中かどうか。多重起動防止に使う</summary>
        public bool IsLoading { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // 既に他のSceneLoaderが存在する（＝手動配置と自動生成が重複した等）ので自分は破棄
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 起動直後は画面が見えている状態（フェードで覆われていない状態）にしておく
            if (_fadeCanvasGroup != null)
            {
                _fadeCanvasGroup.alpha = 0f;
                _fadeCanvasGroup.blocksRaycasts = false;
            }

            HideLoadingIndicator();
        }

        /// <summary>シーン名で遷移する（フェードなし・最小構成）</summary>
        public UniTask LoadSceneAsync(string sceneName, CancellationToken ct = default)
            => LoadSceneAsync(sceneName, useFade: false, ct: ct);

        /// <summary>
        /// シーン遷移の本体。
        /// フェードアウト → シーンロード（最低表示時間待ち含む） → フェードイン、の順で処理する。
        /// </summary>
        /// <param name="sceneName">読み込むシーン名（Build Settingsに追加されていること）</param>
        /// <param name="useFade">フェードイン・アウトを使うか</param>
        /// <param name="useLoadingIndicator">ロード中にLoading Indicatorを表示するか。falseならLoading Indicatorが割り当て済みでも一切触らない</param>
        /// <param name="fadeOutDuration">フェードアウトの秒数。0未満を指定するとデフォルト値を使用</param>
        /// <param name="fadeInDuration">フェードインの秒数。0未満を指定するとデフォルト値を使用</param>
        /// <param name="progress">ロード進捗(0〜1)の通知先。不要ならnullでよい</param>
        /// <param name="minimumDuration">ロードが一瞬で終わっても、最低この秒数はロード扱いにする</param>
        /// <param name="ct">キャンセルトークン</param>
        public async UniTask LoadSceneAsync(
            string sceneName,
            bool useFade,
            bool useLoadingIndicator = false,
            float fadeOutDuration = -1f,
            float fadeInDuration = -1f,
            IProgress<float> progress = null,
            float minimumDuration = 0.0f,
            CancellationToken ct = default)
        {
            // 多重起動防止：ロード中に別の遷移リクエストが来ても無視する
            if (IsLoading)
            {
                Debug.LogWarning($"[SceneLoader] 既にロード中のため無視しました: {sceneName}");
                return;
            }

            IsLoading = true;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct, destroyCancellationToken);
            var linkedCt = linkedCts.Token;

            try
            {
                if (useFade)
                {
                    float duration = fadeOutDuration < 0f ? _defaultFadeOutDuration : fadeOutDuration;
                    await FadeOutAsync(duration, linkedCt);
                }

                // フェードで画面が覆われている（＝見えていない）間だけNow Loading的な表示を出す
                if (useLoadingIndicator) ShowLoadingIndicator();

                var op = SceneManager.LoadSceneAsync(sceneName);
                if (op == null)
                {
                    // sceneNameがBuild SettingsのScene Listに登録されていない場合などにnullが返る
                    Debug.LogError(
                        $"[SceneLoader] シーン '{sceneName}' をロードできませんでした。" +
                        "File > Build Profiles（または旧 File > Build Settings）の Scene List に" +
                        "このシーンが追加されているか、シーン名の綴り・大文字小文字が一致しているか確認してください。");

                    if (useLoadingIndicator) HideLoadingIndicator();

                    // フェードアウト済みなら画面が黒いまま固まらないよう、フェードインして復帰する
                    if (useFade)
                    {
                        float fadeBackDuration = fadeInDuration < 0f ? _defaultFadeInDuration : fadeInDuration;
                        await FadeInAsync(fadeBackDuration, linkedCt);
                    }
                    return;
                }
                op.allowSceneActivation = false;

                float startTime = Time.realtimeSinceStartup;
                progress?.Report(0.0f);

                // ロードが90%完了するまで待つ（残り10%はallowSceneActivationを待っている）
                while (true)
                {
                    float elapsed = Time.realtimeSinceStartup - startTime;
                    float loadRatio = Mathf.Clamp01(op.progress / 0.9f);
                    float timeRatio = minimumDuration <= 0.0f ? 1.0f : Mathf.Clamp01(elapsed / minimumDuration);

                    // 実進捗と経過時間の遅いほうに合わせると、ゲージが一瞬で振り切れない
                    progress?.Report(Mathf.Min(loadRatio, timeRatio));

                    if (op.progress >= 0.9f && elapsed >= minimumDuration) break;

                    await UniTask.Yield(PlayerLoopTiming.Update, linkedCt);
                }

                progress?.Report(1.0f);

                op.allowSceneActivation = true;
                await UniTask.WaitUntil(() => op.isDone, cancellationToken: linkedCt);

                // 新しいシーンの読み込みが終わったので、フェードインで見せる前にNow Loading表示を消す
                if (useLoadingIndicator) HideLoadingIndicator();

                if (useFade)
                {
                    float duration = fadeInDuration < 0f ? _defaultFadeInDuration : fadeInDuration;
                    await FadeInAsync(duration, linkedCt);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[SceneLoader] ロードがキャンセルされました: {sceneName}");
                throw;
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ==================================================
        // フェード処理本体
        // ==================================================

        private UniTask FadeOutAsync(float duration, CancellationToken ct)
            => FadeAsync(0f, 1f, duration, ct);

        private UniTask FadeInAsync(float duration, CancellationToken ct)
            => FadeAsync(1f, 0f, duration, ct);

        private async UniTask FadeAsync(float from, float to, float duration, CancellationToken ct)
        {
            if (_fadeCanvasGroup == null)
            {
                Debug.LogWarning("[SceneLoader] _fadeCanvasGroup が未設定のためフェードをスキップしました");
                return;
            }

            // フェード中は下のUIへの入力を止める
            _fadeCanvasGroup.blocksRaycasts = true;

            if (duration <= 0f)
            {
                _fadeCanvasGroup.alpha = to;
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime; // ポーズ中(timeScale=0)でも動くように
                    float t = Mathf.Clamp01(elapsed / duration);
                    _fadeCanvasGroup.alpha = Mathf.Lerp(from, to, t);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                _fadeCanvasGroup.alpha = to;
            }

            // 完全に透明（画面が見えている状態）になったら入力を通す
            _fadeCanvasGroup.blocksRaycasts = to > 0f;
        }

        // ==================================================
        // ローディング表示のON/OFF
        // ==================================================

        private void ShowLoadingIndicator()
        {
            if (_loadingIndicator != null) _loadingIndicator.SetActive(true);
        }

        private void HideLoadingIndicator()
        {
            if (_loadingIndicator != null) _loadingIndicator.SetActive(false);
        }

        // ==================================================
        // よく使うシーンへのショートカット（必要なら使う）
        // ==================================================

        // public UniTask LoadTitle(bool useFade = true, CancellationToken ct = default)
        //     => LoadSceneAsync("Title", useFade, ct: ct);
        // public UniTask LoadLobby(bool useFade = true, CancellationToken ct = default)
        //     => LoadSceneAsync("Lobby", useFade, ct: ct);
    }
}