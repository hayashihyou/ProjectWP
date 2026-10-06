/// <summary>
/// ミニゲームの流れ（今どの段階か）を表す。サーバーが全員に同期する。
/// byte にして送るデータ量を小さくしている（4バイト → 1バイト）。
/// </summary>
/// 


public enum MinigamePhase : byte
{
    WaitingForPlayers,    // 全員のシーン読み込み完了待ち
    Intro,                // イントロ演出
    Countdown,            // カウントダウン中
    Playing,              // プレイ中(全員が操作可能なのはここだけ)
    RoundEnd,             // ラウンド終了演出中
    RoundReset,           // 次のラウンドの準備中
    GameEnd,              // ゲーム終了演出
    Result                // 結果表示中
}