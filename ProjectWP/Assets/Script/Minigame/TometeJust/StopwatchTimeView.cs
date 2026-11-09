using System.Collections;
using UnityEngine;

/// <summary>
/// 机のストップウォッチの文字盤に時間を表示する。StopwatchDisplay と同じオブジェクト(プレハブの一番上)に付ける。
///
/// 流れ
///   ラウンドの準備   → 「0:00」を出し、黒いカバーを外す
///   計測中           → 進んでいる時間を出す。スタートから数秒たったら、黒いカバーを上からかぶせて隠す
///   ストップ/時間切れ → 文字を止めた秒数にする(カバーをかぶせたまま。まだかぶせていなければ、ここでかぶせる)
///   ラウンド終了     → Finish! の演出が終わるころ(revealDelay 秒後)に、全員一斉にカバーを外して見せる
///   全ラウンド終了   → 画面が暗い間に、合計の誤差に切り替える
///
/// 状態の切り替えは StopwatchDisplay の合図を聞くだけなので、ネットワークのことは考えなくてよい。
/// </summary>
[RequireComponent(typeof(StopwatchDisplay))]
public class StopwatchTimeView : MonoBehaviour
{
    [Header("黒いカバー")]
    [Tooltip("文字盤にかぶせる黒いカバー。空なら子の Watch/Cover を自動で探す")]
    [SerializeField] private Renderer cover;

    [Tooltip("カバーをかぶせる・外すのにかかる秒数")]
    [SerializeField] private float coverDuration = 0.35f;

    [Tooltip("カバーがどれだけ上から降りてくるか(ストップウォッチの中での距離)。大きくすると文字盤からはみ出して見える")]
    [SerializeField] private float coverDropDistance = 0.04f;

    [Header("一斉開示")]
    [Tooltip("ラウンドが終わってから、カバーを外すまでの秒数(Finish! の演出の長さに合わせる)")]
    [SerializeField] private float revealDelay = 1.8f;

    /// <summary>ラウンドが終わってから、カバーを外す(= 止めた時間を見せる)までの秒数。音を合わせるのに使う</summary>
    public float RevealDelay => revealDelay;

    private StopwatchDisplay display;

    // カバーのマテリアル(実行中だけ使う複製。共有のマテリアルを変えると、他の机のカバーまで変わるため)
    private Material coverMaterial;

    // カバーの元の位置(降りてきた後の位置)
    private Vector3 coverRestPosition;

    // カバーをかぶせているか(かぶせ始めたら true)
    private bool covered;

    // 今動いているカバーのアニメーション・開示待ち
    private Coroutine coverRoutine;
    private Coroutine revealRoutine;


    // ==================== 合図を受け取る ====================

    private void Awake()
    {
        display = GetComponent<StopwatchDisplay>();

        if (cover == null)
        {
            Transform t = transform.Find("Watch/Cover");
            if (t != null) { cover = t.GetComponent<Renderer>(); }
        }
        if (cover != null)
        {
            coverMaterial = cover.material;
            coverRestPosition = cover.transform.localPosition;
        }

        SetCover(0f);
    }

    // 文字(TimeText)は StopwatchDisplay の Awake で見つかる。Awake の順番は決まっていないので、Start で書く
    // 最初のラウンドの準備までの間も、ストップウォッチらしく 0:00 を出しておく
    private void Start()
    {
        SetText(StopwatchTimeFormat.Format(0f));
    }

    private void OnDestroy()
    {
        if (coverMaterial != null) { Destroy(coverMaterial); }
    }

    // StopwatchDisplay の合図は OnEnable で登録して、OnDisable で外す
    private void OnEnable()
    {
        display.OnRoundReady += HandleRoundReady;
        display.OnStopped += HandleStopped;
        display.OnTimedOut += HandleStopped;
        display.OnRoundOver += HandleRoundOver;
        display.OnGameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        display.OnRoundReady -= HandleRoundReady;
        display.OnStopped -= HandleStopped;
        display.OnTimedOut -= HandleStopped;
        display.OnRoundOver -= HandleRoundOver;
        display.OnGameOver -= HandleGameOver;
    }

    // 新しいラウンドの準備: 「0:00」にして、カバーをすぐ外す
    private void HandleRoundReady(StopwatchDisplay d)
    {
        StopRoutines();
        covered = false;
        SetCover(0f);
        // プレイヤーがいない机は何も出さない
        SetText(d.IsOccupied ? StopwatchTimeFormat.Format(0f) : "");
    }

    // ストップ・時間切れ: 文字を止めた秒数にする。まだ見えていたら、ここでカバーをかぶせる
    private void HandleStopped(StopwatchDisplay d, float seconds)
    {
        SetText(StopwatchTimeFormat.Format(seconds));
        CoverIfNeeded();
    }

    // ラウンド終了: Finish! の演出が終わるのを待ってから、一斉に見せる
    private void HandleRoundOver(StopwatchDisplay d, float seconds)
    {
        if (revealRoutine != null) { StopCoroutine(revealRoutine); }
        revealRoutine = StartCoroutine(RevealAfterDelay());
    }


    // 全ラウンド終了(画面が暗い間): カバーをすぐ外して、合計の誤差を出す
    private void HandleGameOver(StopwatchDisplay d, float totalError)
    {
        StopRoutines();
        covered = false;
        SetCover(0f);
        SetText(StopwatchTimeFormat.Format(totalError)); // 例: 誤差の合計 1.23秒 → "1:23"
    }


    // ==================== 計測中の表示 ====================

    private void Update()
    {
        if (display.State != StopwatchDisplay.DisplayState.Running) { return; }

        // 進んでいる時間を出す(カバーの下でも書き換えておく)。経過時間がまだ届いていない間(-1)は 0:00 のまま
        SetText(StopwatchTimeFormat.Format(MinigameInfo.ElapsedTime));

        // 見せてよい時間が過ぎたら、カバーをかぶせる
        if (!display.ShouldShowRunningTime) { CoverIfNeeded(); }
    }


    // ==================== ここから下は内部の処理 ====================

    private void SetText(string text)
    {
        if (display.TimeText != null) { display.TimeText.text = text; }
    }

    // まだかぶせていなければ、カバーをかぶせ始める
    private void CoverIfNeeded()
    {
        if (covered) { return; }
        covered = true;
        PlayCover(0f, 1f);
    }

    private IEnumerator RevealAfterDelay()
    {
        yield return new WaitForSeconds(revealDelay);

        // 止めた秒数を出し直してから外す(時間切れの人は、ここで初めて秒数が入っていることもある)
        if (display.StoppedSeconds >= 0f) { SetText(StopwatchTimeFormat.Format(display.StoppedSeconds)); }
        if (covered) { PlayCover(1f, 0f); }
        covered = false;
        revealRoutine = null;
    }

    private void PlayCover(float from, float to)
    {
        if (coverRoutine != null) { StopCoroutine(coverRoutine); }
        coverRoutine = StartCoroutine(CoverRoutine(from, to));
    }

    // カバーの濃さを from → to に変える。濃くなるときは上から降りてきて、薄くなるときは上へ戻っていく
    private IEnumerator CoverRoutine(float from, float to)
    {
        float t = 0f;
        while (t < coverDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / coverDuration);
            float easeOut = 1f - (1f - k) * (1f - k); // 最初に速く、最後はゆっくり
            SetCover(Mathf.Lerp(from, to, easeOut));
            yield return null;
        }
        SetCover(to);
        coverRoutine = null;
    }

    // カバーの濃さ(0 = 見えない・上にある、1 = 真っ黒・文字盤の上)を決める
    private void SetCover(float amount)
    {
        if (cover == null) { return; }

        cover.enabled = amount > 0f;
        cover.transform.localPosition = coverRestPosition + Vector3.up * (coverDropDistance * (1f - amount));

        if (coverMaterial != null)
        {
            Color c = coverMaterial.GetColor("_BaseColor");
            c.a = amount;
            coverMaterial.SetColor("_BaseColor", c);
        }
    }

    private void StopRoutines()
    {
        if (coverRoutine != null) { StopCoroutine(coverRoutine); coverRoutine = null; }
        if (revealRoutine != null) { StopCoroutine(revealRoutine); revealRoutine = null; }
    }
}
