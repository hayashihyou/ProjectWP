using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 音声ファイルを右クリック →「SoundLibrary に登録」で、SoundLibrary.asset に1行追加する。
///
///   ・ID はファイル名から自動で付ける(例: SE_Jump.wav → SE_Jump)
///   ・Assets/Audio/BGM フォルダの中なら BGM 用、それ以外は SE 用の初期設定にする
///   ・すでに登録済みのファイル / 同じIDが既にあるときは、登録せずに知らせる
///   ・何個かまとめて選んで登録してもよい
///
/// 登録後は SoundLibrary.asset が選ばれるので、一番下に増えた行に「使用場面」「使用用途」を入れる。
/// </summary>
public static class SoundLibraryRegister
{
    private const string LibraryPath = "Assets/Resources/SoundLibrary.asset";
    private const string MenuPath = "Assets/SoundLibrary に登録";

    [MenuItem(MenuPath, false, 1000)]
    private static void Register()
    {
        var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
        if (library == null)
        {
            EditorUtility.DisplayDialog("SoundLibrary に登録", $"{LibraryPath} が見つかりません。", "OK");
            return;
        }

        var added = new List<string>();
        var skipped = new List<string>();

        // 既に登録されている ID とファイル
        var ids = new HashSet<string>();
        var clips = new HashSet<AudioClip>();
        foreach (SoundEntry e in library.Entries)
        {
            if (e == null) { continue; }
            if (!string.IsNullOrEmpty(e.id)) { ids.Add(e.id); }
            if (e.clip != null) { clips.Add(e.clip); }
        }

        // SerializedObject 経由で書き換える(Undo で元に戻せる・保存される)
        var so = new SerializedObject(library);
        SerializedProperty list = so.FindProperty("_entries");

        foreach (AudioClip clip in SelectedClips())
        {
            string id = MakeId(clip.name);

            if (clips.Contains(clip)) { skipped.Add($"{clip.name}(もう登録されています)"); continue; }
            if (ids.Contains(id)) { skipped.Add($"{clip.name}(ID「{id}」が他の音と重なっています)"); continue; }

            bool isBgm = AssetDatabase.GetAssetPath(clip).Replace('\\', '/').Contains("/Audio/BGM/");

            list.arraySize++;
            SerializedProperty e = list.GetArrayElementAtIndex(list.arraySize - 1);

            // 「＋」で増やしたときと違い、1つ上の行の値がコピーされないよう、全部の項目を入れ直す
            e.FindPropertyRelative("id").stringValue = id;
            e.FindPropertyRelative("clip").objectReferenceValue = clip;
            e.FindPropertyRelative("category").enumValueIndex = isBgm ? (int)SoundCategory.Bgm : (int)SoundCategory.Se;
            e.FindPropertyRelative("sceneLabel").stringValue = "";
            e.FindPropertyRelative("usageLabel").stringValue = "";
            e.FindPropertyRelative("volume").floatValue = 1f;
            e.FindPropertyRelative("pitch").floatValue = 1f;
            e.FindPropertyRelative("loop").boolValue = isBgm;
            e.FindPropertyRelative("loopFadeSeconds").floatValue = 0f;
            e.FindPropertyRelative("fadeInSeconds").floatValue = isBgm ? 0.5f : 0f;
            e.FindPropertyRelative("fadeOutSeconds").floatValue = isBgm ? 0.5f : 0f;
            e.FindPropertyRelative("randomizePitch").boolValue = false;
            e.FindPropertyRelative("randomPitchRange").floatValue = 0.05f;
            e.FindPropertyRelative("minInterval").floatValue = isBgm ? 0f : 0.05f;

            ids.Add(id);
            clips.Add(clip);
            added.Add($"{id}({(isBgm ? "BGM" : "SE")})");
        }

        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssetIfDirty(library);

        // 結果を知らせて、SoundLibrary を選んだ状態にする(続けて日本語名を入れられるように)
        var message = new StringBuilder();
        if (added.Count > 0)
        {
            message.AppendLine("登録しました(SoundLibrary の一番下に増えています):");
            foreach (string s in added) { message.AppendLine("  ・" + s); }
            message.AppendLine();
            message.AppendLine("Scene Label(使用場面)と Usage Label(使用用途)を入れてください。");
        }
        if (skipped.Count > 0)
        {
            if (message.Length > 0) { message.AppendLine(); }
            message.AppendLine("登録しなかったもの:");
            foreach (string s in skipped) { message.AppendLine("  ・" + s); }
        }
        EditorUtility.DisplayDialog("SoundLibrary に登録", message.ToString(), "OK");

        Selection.activeObject = library;
        EditorGUIUtility.PingObject(library);
    }

    // 音声ファイルを選んでいるときだけメニューを押せるようにする
    [MenuItem(MenuPath, true)]
    private static bool CanRegister()
    {
        foreach (AudioClip _ in SelectedClips()) { return true; }
        return false;
    }

    private static IEnumerable<AudioClip> SelectedClips()
    {
        foreach (Object o in Selection.objects)
        {
            if (o is AudioClip clip) { yield return clip; }
        }
    }

    // ファイル名をIDにする。空白などコードで書きにくい文字は「_」にする
    private static string MakeId(string fileName)
    {
        var sb = new StringBuilder(fileName.Length);
        foreach (char c in fileName.Trim())
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        return sb.ToString();
    }
}
