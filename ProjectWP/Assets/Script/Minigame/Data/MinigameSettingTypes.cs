/*
 * MinigameSettingTypes.cs
 * MinigameBaseのインスペクターで選ぶ設定の種類をまとめたファイル
 */


/// <summary>
/// プレイヤーの値が何を表すか。表示に使う 
/// </summary>
public enum MinigameValueKind : byte
{
    Score,  // 得点
    Time,   // 時間
    Count   // 回数、個数
}

/// <summary>
/// 勝利条件について 
/// </summary>
public enum MinigameWinOrder : byte
{
    HigherIsBetter, // 大きいほど勝ち
    LowerIsBetter   // 小さいほど勝ち
}

/// <summary>
/// ラウンドの値のまとめ方
/// </summary>
public enum MinigameRoundAggregation : byte
{
    Sum,        // 全ラウンドの合計
    Best,       // 一番良かったラウンドの値
    Last,       // 最後のラウンドの値
    RankPoints  // ラウンドごとの順位を勝ち点に変えて合計
}

/// <summary>
/// 結果画面の後にホストが選ぶ次の行先
/// </summary>
public enum MinigameNextAction : byte
{
    Retry,          // もう一度プレイ
    ToStageSelect,  // ステージ選択へ
    ToTitle         // タイトルへ(部屋を解散)
}