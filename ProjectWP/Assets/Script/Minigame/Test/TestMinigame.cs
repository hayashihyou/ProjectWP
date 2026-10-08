using UnityEngine;

/// <summary>
/// 基盤の動作確認用ミニゲーム
/// </summary>
public class TestMinigame : MinigameBase
{
    // 確認用：画面の左上に、ラウンド・フェーズ・残り時間を表示する
    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 600, 40), $"ラウンド: {CurrentRound}/{TotalRounds}   フェーズ: {Phase}   残り: {RemainingTime:0.0}秒");
    }
}

