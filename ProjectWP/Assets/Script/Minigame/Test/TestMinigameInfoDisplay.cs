using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 確認用の UI。MinigameInfo だけを使って(Netcode を一切使わずに)情報を表示し、イベントをログに出す。
/// UI 担当が書くスクリプトの見本も兼ねる。シーンのどこに置いてもよい
/// </summary>
public class TestMinigameInfoDisplay : MonoBehaviour
{
    // 出来事(イベント)は OnEnable で登録し、OnDisable で外す
    private void OnEnable()
    {
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
        MinigameInfo.OnCountdown += HandleCountdown;
        MinigameInfo.OnRoundStarted += HandleRoundStarted;
        MinigameInfo.OnRoundEnded += HandleRoundEnded;
        MinigameInfo.OnPlayerValueChanged += HandlePlayerValueChanged;
        MinigameInfo.OnPlayerFinished += HandlePlayerFinished;
        MinigameInfo.OnGameFinished += HandleGameFinished;
        MinigameInfo.OnCustomEvent += HandleCustomEvent;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
        MinigameInfo.OnCountdown -= HandleCountdown;
        MinigameInfo.OnRoundStarted -= HandleRoundStarted;
        MinigameInfo.OnRoundEnded -= HandleRoundEnded;
        MinigameInfo.OnPlayerValueChanged -= HandlePlayerValueChanged;
        MinigameInfo.OnPlayerFinished -= HandlePlayerFinished;
        MinigameInfo.OnGameFinished -= HandleGameFinished;
        MinigameInfo.OnCustomEvent -= HandleCustomEvent;
    }

    private void HandlePhaseChanged(MinigamePhase phase) => Debug.Log($"[UI] フェーズ: {phase}");
    private void HandleCountdown(int secondsLeft) => Debug.Log(secondsLeft > 0 ? $"[UI] {secondsLeft}" : "[UI] スタート！");
    private void HandleRoundStarted(int round) => Debug.Log($"[UI] ラウンド{round} 開始");
    private void HandleRoundEnded(int round) => Debug.Log($"[UI] ラウンド{round} 終了");
    private void HandlePlayerValueChanged(int slot, float delta, float newValue) => Debug.Log($"[UI] {slot + 1}P {delta:+0;-0} → {MinigameInfo.FormatValue(newValue)}");
    private void HandlePlayerFinished(int slot) => Debug.Log($"[UI] {slot + 1}P ストップ！");
    private void HandleGameFinished(IReadOnlyList<MinigameRankEntry> result) => Debug.Log($"[UI] ゲーム終了。1位は {result[0].SlotIndex + 1}P");
    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
        => Debug.Log($"[UI] 独自の出来事: {eventName}  値: {value}" + (slot >= 0 ? $"  ({slot + 1}P)" : ""));

    // 今の値(プロパティ)は、毎フレーム読んで表示する
    private void OnGUI()
    {
        if (!MinigameInfo.IsActive) { return; }

        string remaining = MinigameInfo.RemainingTime >= 0 ? $"{MinigameInfo.RemainingTime:0.0}秒" : "なし";
        GUI.Label(new Rect(10, 10, 700, 25),
            $"ラウンド: {MinigameInfo.CurrentRound}/{MinigameInfo.TotalRounds}   フェーズ: {MinigameInfo.Phase}   残り: {remaining}   操作できる: {MinigameInfo.CanControl}");

        string me = MinigameInfo.HasLocalPlayer ? $"{MinigameInfo.LocalPlayer.SlotIndex + 1}P" : "なし";
        GUI.Label(new Rect(10, 35, 700, 25), $"自分: {me}   目標: {MinigameInfo.GetInfo("目標")}");

        for (int i = 0; i < MinigameInfo.PlayerCount; i++)
        {
            MinigamePlayerState player = MinigameInfo.GetPlayer(i);
            GUI.Label(new Rect(10, 60 + i * 25, 700, 25),
                $"{player.SlotIndex + 1}P  今回: {MinigameInfo.FormatValue(player.RoundValue)}  合計: {MinigameInfo.FormatValue(player.TotalValue)}  終えた: {player.IsFinished}");
        }

        IReadOnlyList<MinigameRankEntry> result = MinigameInfo.LastResult;
        if (result == null) { return; }

        float y = 70 + MinigameInfo.PlayerCount * 25;
        GUI.Label(new Rect(10, y, 700, 25), "【結果】");
        for (int i = 0; i < result.Count; i++)
        {
            GUI.Label(new Rect(10, y + 25 + i * 25, 700, 25),
                $"{result[i].Rank}位  {result[i].SlotIndex + 1}P  {MinigameInfo.FormatValue(result[i].Value)}");
        }
    }
}
