using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゲームの流れ(MinigameInfo の出来事)を受け取って、机のストップウォッチ(StopwatchDisplay)の状態を切り替える。
/// シーンの StopwatchDesks(4つの机の親)に付ける。子にあるストップウォッチを自動で集める。
///
/// ネットワークについて
/// ・「誰が何秒で止めたか」は、サーバーの StopwatchMinigame が ServerSendEvent で全員に送っている
/// ・その出来事は MinigameInfo が各端末で受け取るので、ここでは MinigameInfo のイベントを聞くだけでよい
///   (このスクリプトに NetworkObject は不要。全員の端末で同じように動く)
///
/// 流れとストップウォッチの状態
///   次のラウンドの準備(RoundReset)・ラウンド開始演出(RoundIntro) → PrepareRound … 待機に戻す・光を消す
///   プレイ開始(OnRoundStarted)   → StartRunning  … 計測中
///   「ストップ」の出来事          → Stop          … その人の机だけ光る
///   「時間切れ」の出来事          → TimeUp        … 押さなかった人(ラウンド終了の直後に届く)
///   ラウンド終了(OnRoundEnded)   → EndRound      … 止めた時間を見せてよいタイミング(時間切れが届くのを2フレーム待ってから)
///   ゲーム終了演出(GameEnd)       → EndGame       … 合計の誤差を見せる(最後のラウンドの後、画面が暗い間に切り替わる)
/// </summary>
public class StopwatchDeskDisplays : MonoBehaviour
{
    // 枠番号(0 = 1P) → その机のストップウォッチ
    private readonly Dictionary<int, StopwatchDisplay> displays = new Dictionary<int, StopwatchDisplay>();


    // ==================== UI担当が使えるもの ====================

    /// <summary>枠番号(0 = 1P)から、その机のストップウォッチを探す。見つかれば true</summary>
    public bool TryGetDisplay(int slot, out StopwatchDisplay display) => displays.TryGetValue(slot, out display);

    /// <summary>全部の机のストップウォッチ(1P, 2P … の順とは限らない。SlotIndex で見分ける)</summary>
    public IEnumerable<StopwatchDisplay> AllDisplays => displays.Values;


    // ==================== ここから下は内部の処理 ====================

    // 子の机にあるストップウォッチを、枠番号ごとに登録する
    // (枠番号は StopwatchDisplay の Awake で決まる。Awake の順番は決まっていないので、登録は Start で行う)
    private void Start()
    {
        foreach (StopwatchDisplay d in GetComponentsInChildren<StopwatchDisplay>(true))
        {
            if (d.SlotIndex < 0) { continue; }
            if (displays.ContainsKey(d.SlotIndex))
            {
                Debug.LogWarning($"[StopwatchDeskDisplays] {d.SlotIndex + 1}P のストップウォッチが2つあります({d.name})");
                continue;
            }
            displays.Add(d.SlotIndex, d);
        }
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
    }

    // 次のラウンドの準備(画面が暗い間)と、ラウンド開始演出になったら、全部の机を待機に戻す
    // (暗い間に戻しておくと、明るくなったときにはもう 0:00 になっている。1ラウンド目は暗転がないので、ラウンド開始演出で戻す)
    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        // 最後のラウンドの後(画面が暗い間に GameEnd になる)は、合計の誤差に切り替える
        if (phase == MinigamePhase.GameEnd)
        {
            ShowTotals();
            return;
        }

        if (phase != MinigamePhase.RoundReset && phase != MinigamePhase.RoundIntro) { return; }

        foreach (StopwatchDisplay d in displays.Values)
        {
            // その枠にプレイヤーがいるか(2人で遊ぶときの 3P・4P の机などは false になる)
            bool occupied = MinigameInfo.TryGetPlayerBySlot(d.SlotIndex, out _);
            d.PrepareRound(occupied);
        }
    }

    // プレイヤーがいる机に、その人の合計の値(= 3ラウンドの誤差の合計)を出す
    private void ShowTotals()
    {
        foreach (StopwatchDisplay d in displays.Values)
        {
            if (!MinigameInfo.TryGetPlayerBySlot(d.SlotIndex, out MinigamePlayerState player)) { continue; }
            d.EndGame(player.TotalValue);
        }
    }

    // プレイが始まったら、プレイヤーがいる机を計測中にする
    private void HandleRoundStarted(int round)
    {
        foreach (StopwatchDisplay d in displays.Values)
        {
            if (d.IsOccupied) { d.StartRunning(); }
        }
    }

    // ラウンドが終わったら、プレイヤーがいる机をラウンド終了にする
    // 「時間切れ」の出来事はラウンド終了の直後に届くので、2フレーム待ってから切り替える
    // (待たないと、時間切れの人だけ「止めた秒数がまだない」状態でラウンド終了になってしまう)
    private void HandleRoundEnded(int round)
    {
        StartCoroutine(EndRoundAfterFrames());
    }

    private System.Collections.IEnumerator EndRoundAfterFrames()
    {
        yield return null;
        yield return null;
        foreach (StopwatchDisplay d in displays.Values)
        {
            if (d.IsOccupied) { d.EndRound(); }
        }
    }

    // ミニゲーム担当が送った出来事(「ストップ」「時間切れ」)を、その人の机に伝える
    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
    {
        if (!displays.TryGetValue(slot, out StopwatchDisplay d)) { return; }

        if (eventName == StopwatchMinigame.StopEventName)
        {
            d.Stop(value.FloatValue);   // 値 = 止めた秒数
        }
        else if (eventName == StopwatchMinigame.TimeUpEventName)
        {
            d.TimeUp(value.FloatValue); // 値 = 時間切れの秒数
        }
    }
}
