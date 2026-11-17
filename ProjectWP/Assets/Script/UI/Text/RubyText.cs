using System.Text.RegularExpressions;
using AutoRuby;
using TMPro;

/// <summary>
/// コードから「{漢字|よみ}」の形の文字(ルビ付きの文字)を TMP に入れるための道具。
/// 例: label.SetRubyText("{参加者|さんかしゃ}リスト")
/// </summary>
public static class RubyText
{
    // AutoRubyTMP.SetTextWithRuby と同じ書き方「{漢字|よみ}」
    private static readonly Regex RubyMarkup = new Regex(@"\{([^{}|]+)\|[^{}|]*\}");

    /// <summary>
    /// AutoRubyTMP が付いていればルビ付きで表示する。
    /// 付いていなければ「よみ」を取り除いて、漢字だけを表示する。
    /// </summary>
    public static void SetRubyText(this TMP_Text text, string markup)
    {
        if (text == null) return;

        if (text.TryGetComponent(out AutoRubyTMP ruby))
        {
            ruby.SetTextWithRuby(markup);
        }
        else
        {
            text.text = Strip(markup);
        }
    }

    /// <summary>「よみ」を取り除いて、漢字だけの文字にする(ログに出すときなど)</summary>
    public static string Strip(string markup)
    {
        return markup == null ? null : RubyMarkup.Replace(markup, "$1");
    }
}
