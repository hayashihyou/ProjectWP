using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// リザルト画面の流れ(リザルトUIシーン MinigameResult に1つ置く)
///
///   1. 背景(MinigameResultBackground: 斜めのカメラ + ぼかし)ができるのを待つ
///   2. リザルトのテクスチャを出し、プレイヤーを1位〜4位の順に並べる
///   3. Show Seconds 秒たったら、テクスチャとプレイヤーを隠す
///   4. 「もう一度遊ぶ」「ミニゲーム選択にもどる」「タイトルに戻る」のボタンを出す
///
/// ボタンを押して行き先を決められるのはホストだけ。参加者の画面ではボタンを押せず、「ホストが選んでいます」を出す
/// </summary>
public class MinigameResultScreen : MonoBehaviour
{
    [Header("リザルトの表示")]
    [Tooltip("リザルトのテクスチャ(SampleScene の ResultUI をコピーしたもの)。最初は表示しておいてよい(演出側の Hide On Awake で隠れる)")]
    [SerializeField] private GameObject resultRoot;

    [Tooltip("プレイヤーを並べる担当")]
    [SerializeField] private ResultPlayerLineup lineup;

    [Tooltip("リザルトを出すときに呼ぶ(例: GameTitleCanvas/Panel の SlideDownFadeIn.Play)。順位の文字は ResultRankSpot の On Show で再生する")]
    [SerializeField] private UnityEvent onResultShow = new UnityEvent();

    [Tooltip("リザルトを見せておく秒数。過ぎたらテクスチャとプレイヤーを隠してボタンを出す")]
    [SerializeField] private float showSeconds = 5f;

    [Tooltip("背景(カメラ移動・ぼかし)ができるのを待つ最大の秒数。背景が無いシーンでは、この秒数たったら出す")]
    [SerializeField] private float backgroundMaxWaitSeconds = 3f;

    [Header("ボタン")]
    [Tooltip("ボタン3つの親。最初は非表示にしておく")]
    [SerializeField] private GameObject menuRoot;

    [SerializeField] private Button retryButton;        // もう一度遊ぶ
    [SerializeField] private Button stageSelectButton;  // ミニゲーム選択にもどる
    [SerializeField] private Button titleButton;        // タイトルに戻る

    [Tooltip("参加者(ホスト以外)の画面に出す「ホストが選んでいます」。空でもよい")]
    [SerializeField] private GameObject waitingForHostLabel;

    [Header("切り替わりの演出")]
    [Tooltip("リザルトのテクスチャをフェードアウトし、プレイヤーを縮めて消す秒数")]
    [SerializeField, Min(0.01f)] private float resultFadeOutSeconds = 0.5f;

    [Tooltip("リザルトが消えてから、ボタンが出始めるまでの秒数")]
    [SerializeField, Min(0f)] private float menuDelaySeconds = 0.15f;

    [Tooltip("ボタン1つが出きるまでの秒数")]
    [SerializeField, Min(0f)] private float menuItemSeconds = 0.35f;

    [Tooltip("ボタンを上から順に出すときの間隔(秒)。0 = 全部同時")]
    [SerializeField, Min(0f)] private float menuItemInterval = 0.08f;

    [Tooltip("ボタンが出始めるときの大きさ(1 = 元の大きさ)")]
    [SerializeField, Range(0f, 1f)] private float menuItemStartScale = 0.6f;


    private void Awake()
    {
        if (menuRoot != null) menuRoot.SetActive(false);
        if (waitingForHostLabel != null) waitingForHostLabel.SetActive(false);

        if (retryButton != null) retryButton.onClick.AddListener(() => Choose(MinigameNextAction.Retry));
        if (stageSelectButton != null) stageSelectButton.onClick.AddListener(() => Choose(MinigameNextAction.ToStageSelect));
        if (titleButton != null) titleButton.onClick.AddListener(() => Choose(MinigameNextAction.ToTitle));
    }

    private void OnEnable()
    {
        MinigameInfo.OnNextChosen += HandleNextChosen;
    }

    private void OnDisable()
    {
        MinigameInfo.OnNextChosen -= HandleNextChosen;
    }

    private void Start()
    {
        StartCoroutine(Sequence());
    }


    private IEnumerator Sequence()
    {
        // 1. 背景ができるのを待つ(このシーンは Result になってから読み込まれるので、もうできていることもある)
        float waited = 0f;
        while (!MinigameResultBackground.IsReady && waited < backgroundMaxWaitSeconds)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // 結果が届くのも待つ(RPC とフェーズの同期はわずかにずれることがある)
        waited = 0f;
        while (!MinigameRanking.HasResult && waited < backgroundMaxWaitSeconds)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // 2. リザルトのテクスチャとプレイヤーを出す
        if (resultRoot != null) resultRoot.SetActive(true);
        if (lineup != null) lineup.Show();
        onResultShow.Invoke();

        // 3. 一定時間見せたら、テクスチャをフェードアウト・プレイヤーを縮めて隠す
        yield return new WaitForSeconds(showSeconds);
        yield return HideResultAnimated();

        // 4. ボタンを上から順にポンと出す
        if (menuDelaySeconds > 0f) yield return new WaitForSeconds(menuDelaySeconds);
        yield return ShowMenuAnimated();
    }

    // リザルトのテクスチャ(順位の文字・タイトル)をフェードアウトし、並んだプレイヤーを縮めながら隠す
    private IEnumerator HideResultAnimated()
    {
        // テクスチャ: 一番外側の Canvas に CanvasGroup を付けて、まとめて透明にする(入れ子の Canvas にも効く)
        List<CanvasGroup> groups = new List<CanvasGroup>();
        if (resultRoot != null)
        {
            foreach (Canvas canvas in resultRoot.GetComponentsInChildren<Canvas>())
            {
                Transform parent = canvas.transform.parent;
                if (parent != null && parent.GetComponentInParent<Canvas>() != null) continue;

                CanvasGroup group = canvas.GetComponent<CanvasGroup>();
                if (group == null) group = canvas.gameObject.AddComponent<CanvasGroup>();
                groups.Add(group);
            }
        }

        // プレイヤー: 今立っているモデルと、もとの大きさ
        List<Transform> models = new List<Transform>();
        List<Vector3> baseScales = new List<Vector3>();
        if (lineup != null)
        {
            for (int order = 1; order <= 4; order++)
            {
                ResultRankSpot spot = lineup.GetSpot(order);
                if (spot == null || spot.Model == null) continue;
                models.Add(spot.Model.transform);
                baseScales.Add(spot.Model.transform.localScale);
            }
        }

        for (float time = 0f; time < resultFadeOutSeconds; time += Time.deltaTime)
        {
            float rate = time / resultFadeOutSeconds;
            float shrink = 1f - rate * rate; // だんだん速く縮む

            foreach (CanvasGroup group in groups) group.alpha = 1f - rate;
            for (int i = 0; i < models.Count; i++)
            {
                if (models[i] != null) models[i].localScale = baseScales[i] * shrink;
            }
            yield return null;
        }

        if (resultRoot != null) resultRoot.SetActive(false);
        if (lineup != null) lineup.Hide();

        // 次に表示するときのために元に戻しておく(隠した後なので見えない)
        foreach (CanvasGroup group in groups) group.alpha = 1f;
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i] != null) models[i].localScale = baseScales[i];
        }
    }

    // ボタンを上から順に、小さい状態からポンと弾むようにフェードインさせる
    private IEnumerator ShowMenuAnimated()
    {
        bool isHost = MinigameInfo.IsHost;

        // 演出中は押せないようにしておく
        SetButtonsInteractable(false);
        if (waitingForHostLabel != null) waitingForHostLabel.SetActive(!isHost);
        if (menuRoot == null) yield break;

        // 表示する項目(ボタンと「ホストが選んでいます」)を上から順に集め、透明・小さい状態にしておく
        List<Transform> items = new List<Transform>();
        List<CanvasGroup> groups = new List<CanvasGroup>();
        List<Vector3> baseScales = new List<Vector3>();
        foreach (Transform child in menuRoot.transform)
        {
            if (!child.gameObject.activeSelf) continue;

            CanvasGroup group = child.GetComponent<CanvasGroup>();
            if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();

            items.Add(child);
            groups.Add(group);
            baseScales.Add(child.localScale);

            group.alpha = 0f;
            child.localScale = child.localScale * menuItemStartScale;
        }

        menuRoot.SetActive(true);

        float total = menuItemSeconds + menuItemInterval * Mathf.Max(0, items.Count - 1);
        for (float time = 0f; time < total; time += Time.deltaTime)
        {
            for (int i = 0; i < items.Count; i++)
            {
                float rate = menuItemSeconds > 0f ? Mathf.Clamp01((time - menuItemInterval * i) / menuItemSeconds) : 1f;
                groups[i].alpha = rate;
                items[i].localScale = baseScales[i] * Mathf.LerpUnclamped(menuItemStartScale, 1f, EaseOutBack(rate));
            }
            yield return null;
        }

        for (int i = 0; i < items.Count; i++)
        {
            groups[i].alpha = 1f;
            items[i].localScale = baseScales[i];
        }

        // 出そろってから押せるようにする
        SetButtonsInteractable(isHost);

        // コントローラー・キーボードでも選べるように、最初のボタンを選んだ状態にする
        if (isHost && retryButton != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(retryButton.gameObject);
        }
    }

    // 少し行き過ぎてから戻る動き(0→1)
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float t = x - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }

    private void Choose(MinigameNextAction action)
    {
        // 2回押されても遷移が重ならないように、すぐに押せなくする(MinigameBase 側でも防いでいる)
        SetButtonsInteractable(false);
        MinigameInfo.ChooseNext(action);
    }

    // ホストが選んだら、全員の画面でボタンを押せなくする
    private void HandleNextChosen(MinigameNextAction action)
    {
        SetButtonsInteractable(false);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (retryButton != null) retryButton.interactable = interactable;
        if (stageSelectButton != null) stageSelectButton.interactable = interactable;
        if (titleButton != null) titleButton.interactable = interactable;
    }
}