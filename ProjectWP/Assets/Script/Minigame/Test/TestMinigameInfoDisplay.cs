using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
        MinigameInfo.OnNextChosen += HandleNextChosen;
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
        MinigameInfo.OnNextChosen -= HandleNextChosen;
    }

    private void HandlePhaseChanged(MinigamePhase phase, int round) => Debug.Log($"[UI] フェーズ: {phase}(ラウンド {round})");
    private void HandleCountdown(int secondsLeft) => Debug.Log(secondsLeft > 0 ? $"[UI] {secondsLeft}" : "[UI] スタート！");
    private void HandleRoundStarted(int round) => Debug.Log($"[UI] ラウンド{round} 開始");
    private void HandleRoundEnded(int round) => Debug.Log($"[UI] ラウンド{round} 終了");
    private void HandlePlayerValueChanged(int slot, float delta, float newValue) => Debug.Log($"[UI] {slot + 1}P {delta:+0;-0} → {MinigameInfo.FormatValue(newValue)}");
    private void HandlePlayerFinished(int slot) => Debug.Log($"[UI] {slot + 1}P ストップ！");
    private void HandleGameFinished(IReadOnlyList<MinigameRankEntry> result) => Debug.Log($"[UI] ゲーム終了。1位は {result[0].SlotIndex + 1}P");
    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
        => Debug.Log($"[UI] 独自の出来事: {eventName}  値: {value}" + (slot >= 0 ? $"  ({slot + 1}P)" : ""));
    private void HandleNextChosen(MinigameNextAction action) => Debug.Log($"[UI] ホストが次の行き先を選んだ: {action}");

    // 確認用: スペースキーで「Stop」を送る(操作してよいときだけ)。結果のときは R / S / T キーで次の行き先を選ぶ(ホストだけ)
    private void Update()
    {
        if (Keyboard.current == null) { return; }

        if (MinigameInfo.Phase == MinigamePhase.Result)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame) { MinigameInfo.ChooseNext(MinigameNextAction.Retry); }
            if (Keyboard.current.sKey.wasPressedThisFrame) { MinigameInfo.ChooseNext(MinigameNextAction.ToStageSelect); }
            if (Keyboard.current.tKey.wasPressedThisFrame) { MinigameInfo.ChooseNext(MinigameNextAction.ToTitle); }
        }

        if (!Keyboard.current.spaceKey.wasPressedThisFrame) { return; }

        // 押したことと、今送れる状態かを必ずログに出す(届かないときの切り分け用)
        Debug.Log($"[UI] スペースが押された  フェーズ: {MinigameInfo.Phase}  操作できる: {MinigameInfo.CanControl}");
        if (MinigameInfo.CanControl)
        {
            MinigameInfo.SendAction("Stop");
        }
    }

    // 今の値(プロパティ)は、毎フレーム読んで表示する
    private void OnGUI()
    {
        if (!MinigameInfo.IsActive) { return; }

        string remaining = MinigameInfo.RemainingTime >= 0 ? $"{MinigameInfo.RemainingTime:0.0}秒" : "なし";
        string elapsed = MinigameInfo.ElapsedTime >= 0 ? $"{MinigameInfo.ElapsedTime:0.00}秒" : "なし";
        GUI.Label(new Rect(10, 10, 700, 25),
            $"ラウンド: {MinigameInfo.CurrentRound}/{MinigameInfo.TotalRounds}   フェーズ: {MinigameInfo.Phase}   残り: {remaining}   経過: {elapsed}   操作できる: {MinigameInfo.CanControl}");

        string me = MinigameInfo.HasLocalPlayer ? $"{MinigameInfo.LocalPlayer.SlotIndex + 1}P" : "なし";
        GUI.Label(new Rect(10, 35, 700, 25), $"自分: {me}   目標: {MinigameInfo.GetInfo("目標")}");

        for (int i = 0; i < MinigameInfo.PlayerCount; i++)
        {
            MinigamePlayerState player = MinigameInfo.GetPlayer(i);
            GUI.Label(new Rect(10, 60 + i * 25, 700, 25),
                $"{player.SlotIndex + 1}P  今回: {MinigameInfo.FormatValue(player.RoundValue)}  合計: {MinigameInfo.FormatValue(player.TotalValue)}  終えた: {player.IsFinished}");
        }

        // 結果は Result のときだけ表示する(LastResult はシーンをまたいで残るので、「もう一度」の後は前回の結果が入っている)
        IReadOnlyList<MinigameRankEntry> result = MinigameInfo.LastResult;
        if (result == null || MinigameInfo.Phase != MinigamePhase.Result) { return; }

        float y = 70 + MinigameInfo.PlayerCount * 25;
        string hint = MinigameInfo.IsHost ? "  R: もう一度 / S: ステージセレクト / T: タイトル" : "  (ホストが次の行き先を選びます)";
        GUI.Label(new Rect(10, y, 700, 25), "【結果】" + hint);
        for (int i = 0; i < result.Count; i++)
        {
            GUI.Label(new Rect(10, y + 25 + i * 25, 700, 25),
                $"{result[i].Rank}位  {result[i].SlotIndex + 1}P  {MinigameInfo.FormatValue(result[i].Value)}");
        }
    }
}
