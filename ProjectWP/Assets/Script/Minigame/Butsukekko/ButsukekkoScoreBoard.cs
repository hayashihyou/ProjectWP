using TMPro;
using UnityEngine;

/// <summary>
/// 机の上のめくり式スコアボード1人分。ScoreFlipBoard プレハブの一番上に付ける。
///
/// SetScore(点数) を呼ぶと、2枚のカード(十の位・一の位)の数字が変わる。
/// 表示できるのは -9 〜 99。マイナスのときは十の位のカードに「-」を出す。
/// 範囲外の点数は、表示だけ端の値に丸める(Score には渡した値がそのまま入る)。
///
/// インスペクターの「最初の点数」を変えると、再生しなくても見た目に反映される(確認用)。
/// </summary>
public class ButsukekkoScoreBoard : MonoBehaviour
{
    private const int MinDisplayScore = -9;
    private const int MaxDisplayScore = 99;

    [Header("どのプレイヤーの板か")]
    [Tooltip("枠番号(0 = 1P, 1 = 2P …)。スコアを書き換える側が、どの板を使うか探すときの目印")]
    [SerializeField] private int slotIndex = 0;

    [Header("数字")]
    [Tooltip("十の位のカードの文字")]
    [SerializeField] private TMP_Text tensDigit;

    [Tooltip("一の位のカードの文字")]
    [SerializeField] private TMP_Text onesDigit;

    [Tooltip("最初に表示しておく点数")]
    [SerializeField] private int initialScore = 0;

    public int SlotIndex => slotIndex;

    /// <summary>今表示している点数(SetScore で渡した値)</summary>
    public int Score { get; private set; }


    private void Awake()
    {
        SetScore(initialScore);
    }

    // インスペクターで値を変えたときに、エディター上の見た目にもすぐ反映する
    private void OnValidate()
    {
        ApplyDigits(initialScore);
    }

    /// <summary>点数を変えて、カードの数字を書き換える</summary>
    public void SetScore(int score)
    {
        Score = score;
        ApplyDigits(score);
    }

    private void ApplyDigits(int score)
    {
        int shown = Mathf.Clamp(score, MinDisplayScore, MaxDisplayScore);

        string tens;
        string ones;
        if (shown < 0)
        {
            // マイナスは「-3」のように、十の位に「-」、一の位に数字を出す
            tens = "-";
            ones = (-shown).ToString();
        }
        else
        {
            // 1桁のときも十の位は「0」を出す(めくり式の板らしく、常に2枚とも数字にする)
            tens = (shown / 10).ToString();
            ones = (shown % 10).ToString();
        }

        if (tensDigit != null) tensDigit.text = tens;
        if (onesDigit != null) onesDigit.text = ones;
    }
}
