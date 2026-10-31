using System.Collections;
using UnityEngine;

/// <summary>
/// 連続ストップウォッチ固有の音を鳴らす。シーンの StopwatchDesks(StopwatchDeskDisplays と同じオブジェクト)に付ける。
/// スタート・フィニッシュ・タイムアップは全ミニゲーム共通なので、MinigameBase が鳴らしている(ここでは鳴らさない)。
///
/// 流れと音
///   「目標決定」の出来事        → 目標時間の表示音
///   プレイ開始                  → 時間が進んでいる音(ループ)。カバーがかぶさって時間が見えなくなったら止める
///   「ストップ」の出来事        → 止める音(誰が止めても同じ音)
///   ラウンド終了                → カバーを外す少し前に「公開前」の音 → 外す瞬間に「公開」の音
///   ゲーム終了演出(GameEnd)    → 合計の誤差を見せるときに「公開」の音
///
/// MinigameInfo の出来事を聞くだけなので、全員の端末で同じように鳴る(NetworkObject は不要)。
/// </summary>
[RequireComponent(typeof(StopwatchDeskDisplays))]
public class StopwatchSounds : MonoBehaviour
{
    [Header("音(SoundLibrary のID。空欄 = 鳴らさない)")]
    [SerializeField] private string targetShowId = "Stopwatch_TargetShow";
    [SerializeField] private string stopId = "Stopwatch_Stop";
    [SerializeField] private string tickId = "Stopwatch_Tick";
    [SerializeField] private string revealBeforeId = "Stopwatch_RevealBefore";
    [SerializeField] private string revealId = "Stopwatch_Reveal";

    [Header("タイミング")]
    [Tooltip("カバーを外す何秒前から「公開前」の音を鳴らすか(外す瞬間に止める)。" +
             "ラウンド終了からカバーを外すまで(StopwatchTimeView の Reveal Delay)より長くすると、ラウンド終了の瞬間から鳴る")]
    [SerializeField] private float revealBeforeLead = 0.8f;

    [Tooltip("ゲーム終了演出になってから、合計の誤差の「公開」の音を鳴らすまでの秒数(画面が明るくなるのに合わせる)")]
    [SerializeField] private float totalRevealDelay = 0.6f;

    private StopwatchDeskDisplays desks;
    private float revealDelay = 1.8f; // StopwatchTimeView から読み直す(見つからなければこの値)

    private SoundHandle tick;
    private SoundHandle revealBefore;
    private Coroutine revealRoutine;

    private void Awake()
    {
        desks = GetComponent<StopwatchDeskDisplays>();

        // カバーを外すタイミングは机の StopwatchTimeView が決めているので、そこから読む(全部の机で同じ値)
        StopwatchTimeView view = GetComponentInChildren<StopwatchTimeView>(true);
        if (view != null) { revealDelay = view.RevealDelay; }
    }

    // イベントは OnEnable で登録して、OnDisable で外す(MinigameInfo の決まり)
    private void OnEnable()
    {
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
        MinigameInfo.OnRoundStarted += HandleRoundStarted;
        MinigameInfo.OnRoundEnded += HandleRoundEnded;
        MinigameInfo.OnCustomEvent += HandleCustomEvent;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
        MinigameInfo.OnRoundStarted -= HandleRoundStarted;
        MinigameInfo.OnRoundEnded -= HandleRoundEnded;
        MinigameInfo.OnCustomEvent -= HandleCustomEvent;
        StopAllSounds();
    }

    // 時間が見えている間だけ、進んでいる音を鳴らす
    private void Update()
    {
        if (tick.IsPlaying && !IsAnyRunningTimeVisible()) { tick.Stop(); }
    }

    private void HandleRoundStarted(int round)
    {
        tick.Stop();
        tick = Play(tickId);
    }

    private void HandleRoundEnded(int round)
    {
        tick.Stop();
        if (revealRoutine != null) { StopCoroutine(revealRoutine); }
        revealRoutine = StartCoroutine(RevealRoutine());
    }

    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        if (phase == MinigamePhase.RoundReset)
        {
            // 次のラウンドの準備に入ったら、前のラウンドの音を残さない
            StopAllSounds();
        }
        else if (phase == MinigamePhase.GameEnd)
        {
            StopAllSounds();
            revealRoutine = StartCoroutine(PlayAfter(totalRevealDelay, revealId));
        }
    }

    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
    {
        if (eventName == StopwatchMinigame.TargetEventName) { Play(targetShowId); }
        else if (eventName == StopwatchMinigame.StopEventName) { Play(stopId); }
    }

    // カバーを外す revealBeforeLead 秒前から「公開前」→ 外す瞬間に止めて「公開」
    private IEnumerator RevealRoutine()
    {
        float lead = Mathf.Clamp(revealBeforeLead, 0f, revealDelay);
        yield return new WaitForSeconds(revealDelay - lead);

        revealBefore = Play(revealBeforeId);
        yield return new WaitForSeconds(lead);

        revealBefore.Stop(0.05f);
        Play(revealId);
        revealRoutine = null;
    }

    private IEnumerator PlayAfter(float seconds, string id)
    {
        yield return new WaitForSeconds(seconds);
        Play(id);
        revealRoutine = null;
    }

    // プレイヤーがいる机のどれかで、進んでいる時間がまだ見えているか
    private bool IsAnyRunningTimeVisible()
    {
        foreach (StopwatchDisplay d in desks.AllDisplays)
        {
            if (d.IsOccupied && d.ShouldShowRunningTime) { return true; }
        }
        return false;
    }

    private void StopAllSounds()
    {
        if (revealRoutine != null) { StopCoroutine(revealRoutine); revealRoutine = null; }
        tick.Stop();
        revealBefore.Stop();
    }

    private static SoundHandle Play(string id)
    {
        return string.IsNullOrEmpty(id) ? SoundHandle.None : SoundManager.Instance.PlaySe(id);
    }
}
