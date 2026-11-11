using System;
using System.Collections.Generic;

/// <summary>
/// 「もう一度再生して確認できる演出」の登録窓口。
/// テスト用パネル(EffectTester)は、ここに登録されたものを再生ボタンとして並べる。
///
/// 登録のしかた(どちらか):
///   ・コードから: 演出を持つクラスの OnEnable で RegisterButton を呼び、
///                 OnDisable で Unregister(this) を呼ぶ。(例: PlayerJoinPresentation)
///   ・コードを書かずに: シーンに ReplayableEffect コンポーネントを置く。
///
/// ここ自体は「一覧を持っているだけ」なので、本番で EffectTester を置かなければ何も表示されない。
/// (登録している側のコードは、EffectTester を消しても壊れない)
/// </summary>
public static class EffectReplayRegistry
{
    /// <summary>登録された1項目(再生ボタン1つ分)</summary>
    public class Entry
    {
        public object Owner;     // 誰が登録したか(まとめて外すときに使う)
        public string Group;     // 見出し(例: "待機所: 参加演出")
        public string Label;     // ボタンの横に出す名前
        public Action OnPressed; // 再生ボタンを押したときに呼ぶ処理
    }

    private static readonly List<Entry> entries = new List<Entry>();

    /// <summary>登録されているすべての項目(登録した順)</summary>
    public static IReadOnlyList<Entry> Entries => entries;

    /// <summary>押すと処理が呼ばれる「再生ボタン」を登録する</summary>
    public static void RegisterButton(object owner, string group, string label, Action onPressed)
    {
        entries.Add(new Entry { Owner = owner, Group = group, Label = label, OnPressed = onPressed });
    }

    /// <summary>指定した持ち主が登録した項目を、すべて外す</summary>
    public static void Unregister(object owner)
    {
        entries.RemoveAll(entry => entry.Owner == owner);
    }
}
