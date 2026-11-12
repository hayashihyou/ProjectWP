using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【テスト用】演出をボタンでもう一度再生して確認するためのパネル。
/// 最初のシーン(01_Title)の空のGameObjectに付けて Play する。シーンをまたいでも残る。
///
/// 画面右側のパネルに、今いるシーンで EffectReplayRegistry に登録されている演出が
/// 見出しごとに並ぶ。▶ を押すとその演出を自分の画面だけでもう一度再生する(他の人の画面には出ない)。
///
/// 演出を一覧に出したいときは、その演出を EffectReplayRegistry に登録する
/// (コードから登録するか、シーンに ReplayableEffect を置く)。
/// 本番ではこのコンポーネントを外すか、このファイル(と.meta)を消すだけでよい。
/// </summary>
public class EffectTester : MonoBehaviour
{
    private const float PanelWidth = 300f;

    // 右上の「タイトルに戻る」ボタン(DebugReturnToTitleButton)と重ならないように、少し下から出す
    private const float PanelTop = 90f;

    private static EffectTester _instance;

    private bool _collapsed = true;
    private Vector2 _scroll;

    private void Awake()
    {
        // シーンをまたいでも残す。別のシーンにも置いてあった場合は、先にあった方を使う
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) { _instance = null; }
    }

    private void OnGUI()
    {
        float x = Screen.width - PanelWidth - 10f;

        // たたんでいる間は、開くボタンだけ出す(ゲーム画面の邪魔にならないように)
        if (_collapsed)
        {
            if (GUI.Button(new Rect(Screen.width - 150f, PanelTop, 140f, 30f), "演出テスト ▼")) { _collapsed = false; }
            return;
        }

        GUILayout.BeginArea(new Rect(x, PanelTop, PanelWidth, Screen.height - PanelTop - 10f), GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("演出テスト(自分の画面だけ)");
        if (GUILayout.Button("たたむ ▲", GUILayout.Width(80))) { _collapsed = true; }
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll);
        DrawEntries();
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    /// <summary>登録されている演出を、見出しごとに並べる</summary>
    private void DrawEntries()
    {
        IReadOnlyList<EffectReplayRegistry.Entry> entries = EffectReplayRegistry.Entries;
        if (entries.Count == 0)
        {
            GUILayout.Label("このシーンには、再生できる演出が登録されていません。");
            return;
        }

        // ボタンを押すと登録の一覧が変わることがある(押した処理でシーンが切り替わるなど)ので、
        // 描いている途中で一覧が変わっても困らないよう、コピーしてから回す
        var snapshot = new List<EffectReplayRegistry.Entry>(entries);

        // 見出しの順番は、最初に登録された順のまま並べる
        var groups = new List<string>();
        foreach (var entry in snapshot)
        {
            if (!groups.Contains(entry.Group)) groups.Add(entry.Group);
        }

        foreach (string group in groups)
        {
            GUILayout.Space(4);
            GUILayout.Label($"■ {group}");

            foreach (var entry in snapshot)
            {
                if (entry.Group != group) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.Label, GUILayout.Width(190));
                if (GUILayout.Button("▶ 再生")) entry.OnPressed();
                GUILayout.EndHorizontal();
            }
        }
    }
}
