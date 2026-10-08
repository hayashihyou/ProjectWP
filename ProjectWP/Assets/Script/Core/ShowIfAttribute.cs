using UnityEngine;

/// <summary>
/// 指定した bool の設定がすべてオンのときだけ、インスペクターにこの項目を表示する。
///
/// 例: [ShowIf(nameof(useFade))] → useFade がオンのときだけ表示
///     [ShowIf(nameof(useFade), nameof(fadeOut))] → 両方オンのときだけ表示
///
/// 表示を切り替えるだけで、値はそのまま残る。条件の数だけ字下げして、どの設定の子かを分かりやすくする。
/// 実際の表示処理は Editor フォルダの ShowIfDrawer が行う。
/// </summary>
public class ShowIfAttribute : PropertyAttribute
{
    /// <summary>オンになっている必要がある bool の設定の名前</summary>
    public readonly string[] ConditionNames;

    public ShowIfAttribute(params string[] conditionNames)
    {
        ConditionNames = conditionNames;
    }
}
