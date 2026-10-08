using UnityEditor;
using UnityEngine;

/// <summary>
/// ShowIfAttribute の表示処理。条件の bool がすべてオンのときだけ項目を描き、オフなら高さを 0 にして隠す。
/// Editor フォルダに置いているので、ゲームのビルドには含まれない。
/// </summary>
[CustomPropertyDrawer(typeof(ShowIfAttribute))]
public class ShowIfDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (!IsVisible(property)) return;

        // 条件の数だけ字下げする(親の設定の下にぶら下がって見えるように)
        int indent = ((ShowIfAttribute)attribute).ConditionNames.Length;
        EditorGUI.indentLevel += indent;
        EditorGUI.PropertyField(position, property, label, true);
        EditorGUI.indentLevel -= indent;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!IsVisible(property))
        {
            // 項目どうしの隙間の分も詰めて、隠した項目の跡が残らないようにする
            return -EditorGUIUtility.standardVerticalSpacing;
        }
        return EditorGUI.GetPropertyHeight(property, label, true);
    }

    private bool IsVisible(SerializedProperty property)
    {
        foreach (string conditionName in ((ShowIfAttribute)attribute).ConditionNames)
        {
            SerializedProperty condition = FindSibling(property, conditionName);
            if (condition == null)
            {
                Debug.LogWarning($"[ShowIf] '{conditionName}' という設定が見つかりません({property.propertyPath})");
                continue;
            }
            if (condition.propertyType == SerializedPropertyType.Boolean && !condition.boolValue) return false;
        }
        return true;
    }

    // 同じクラス(または同じ構造体)の中にある、別の設定を探す
    private static SerializedProperty FindSibling(SerializedProperty property, string name)
    {
        string path = property.propertyPath;
        int lastDot = path.LastIndexOf('.');
        string siblingPath = lastDot < 0 ? name : path.Substring(0, lastDot + 1) + name;
        return property.serializedObject.FindProperty(siblingPath);
    }
}
