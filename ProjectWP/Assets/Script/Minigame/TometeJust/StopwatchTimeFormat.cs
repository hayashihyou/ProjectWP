using UnityEngine;

/// <summary>
/// ストップウォッチの時間の表示の形(秒:1/100秒)を作る。テレビと机の両方で使う。
/// 例: 3.456秒 → "3:45"、10秒 → "10:00"
/// </summary>
public static class StopwatchTimeFormat
{
    /// <summary>秒数を「秒:1/100秒」の文字にする。マイナスは0として扱う</summary>
    public static string Format(float seconds)
    {
        // 1/100秒の単位に直す(本物のストップウォッチと同じく、切り捨てる)
        int hundredths = Mathf.FloorToInt(Mathf.Max(0f, seconds) * 100f);

        int sec = hundredths / 100;   // 秒の部分
        int frac = hundredths % 100;  // 1/100秒の部分

        // 1/100秒は必ず2けたにする(5 → "05")
        return $"{sec}:{frac:00}";
    }
}
