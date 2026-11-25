using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【テスト用】待機所のスポットライト(光の柱)の形と色を、Play中にスライダーで変えて確かめるためのパネル。
/// 02_PlayerJoin の SpotlightTest に付けて使う。
///
///   ・形のスライダー(根元の直径・広がる角度・高さ・濃さ)を動かすと、出ている光の柱すべてにすぐ反映する。
///   ・色は「全員同じ色」か「プレイヤーごとの色」を選び、R/G/Bのスライダーで決める。
///     後から出てきた光の柱にも、同じ形と色を当てる。
///   ・「全部の台座で点ける」を押すと、プレイヤーがいなくても4つの台座すべてに光の柱を出す(確認用)。
///   ・気に入ったら「プレハブに保存」を押すと、光の柱のプレハブに書き込む(エディタのみ)。
///
/// 本番前に SpotlightTest ごとシーンから消せばよい。
/// </summary>
public class PlayerSpotlightTester : MonoBehaviour
{
    private static readonly string[] ColorModeLabels = { "全員同じ色", "プレイヤーの色" };
    private static readonly string[] SlotLabels = { "1P", "2P", "3P", "4P" };

    [Tooltip("待機所の参加演出「スポットライト」。プレハブと出す位置をここから読む。")]
    [SerializeField] private PlayerJoinSpotlight joinSpotlight;

    // スライダーで変える形。最初はプレハブに入っている値を読み込む
    private float bottomDiameter;
    private float spreadAngle;
    private float height;
    private float bottomAlpha;

    // スライダーで変える色。最初はプレハブに入っている値を読み込む
    private PlayerSpotlight.ColorMode colorMode;
    private Color sameColor = Color.white;
    private readonly Color[] playerColors = new Color[PlayerSlotManager.SlotCount];
    private int editingSlot;  // 「プレイヤーの色」で、今どの枠の色を変えているか

    // 「全部の台座で点ける」で出した光の柱(消すときに使う)
    private readonly List<PlayerSpotlight> previewLights = new List<PlayerSpotlight>();

    private bool collapsed;
    private Vector2 scroll;
    private string saveMessage = "";

    private void Start()
    {
        LoadFromPrefab();
    }

    private void Update()
    {
        // 出ている光の柱すべてに、今のスライダーの形と色を当てる
        // (参加してから出てきたものにも当てたいので、毎フレーム当て直す)
        foreach (PlayerSpotlight spotlight in FindObjectsByType<PlayerSpotlight>(FindObjectsSortMode.None))
        {
            // 形は作り直しに手間がかかるので、変わったときだけ
            if (!IsSameShape(spotlight))
            {
                spotlight.SetShape(bottomDiameter, spreadAngle, height, bottomAlpha);
            }
            spotlight.SetColors(colorMode, sameColor, playerColors);
        }
    }

    private bool IsSameShape(PlayerSpotlight spotlight)
    {
        return Mathf.Approximately(spotlight.BottomDiameter, bottomDiameter)
            && Mathf.Approximately(spotlight.SpreadAngle, spreadAngle)
            && Mathf.Approximately(spotlight.Height, height)
            && Mathf.Approximately(spotlight.BottomAlpha, bottomAlpha);
    }

    /// <summary>プレハブに入っている形と色を、スライダーの値として読み込む</summary>
    private void LoadFromPrefab()
    {
        if (joinSpotlight == null || joinSpotlight.SpotlightPrefab == null) return;

        PlayerSpotlight prefab = joinSpotlight.SpotlightPrefab;
        bottomDiameter = prefab.BottomDiameter;
        spreadAngle = prefab.SpreadAngle;
        height = prefab.Height;
        bottomAlpha = prefab.BottomAlpha;

        colorMode = prefab.CurrentColorMode;
        sameColor = prefab.SameColor;
        for (int i = 0; i < playerColors.Length; i++)
        {
            playerColors[i] = prefab.GetPlayerColor(i);
        }
    }

    /// <summary>プレイヤーがいなくても、4つの台座すべてに光の柱を出す</summary>
    private void ShowPreviewLights()
    {
        if (joinSpotlight == null || joinSpotlight.SpotlightPrefab == null) return;
        if (PlayerSlotManager.Instance == null) return;

        HidePreviewLights();
        for (int i = 0; i < PlayerSlotManager.SlotCount; i++)
        {
            Vector3 position = PlayerSlotManager.Instance.GetSpawnPosition(i) + joinSpotlight.PositionOffset;
            PlayerSpotlight spotlight = Instantiate(joinSpotlight.SpotlightPrefab, position, Quaternion.identity, transform);
            spotlight.SetSlotIndex(i);  // 「プレイヤーの色」のとき、その台座の枠の色になる
            spotlight.TurnOn();
            previewLights.Add(spotlight);
        }
    }

    private void HidePreviewLights()
    {
        foreach (PlayerSpotlight spotlight in previewLights)
        {
            if (spotlight != null) spotlight.TurnOffAndDestroy();
        }
        previewLights.Clear();
    }

    // 画面右上のテスト用パネル(「タイトルに戻る」ボタンと「演出テスト」の下に出す)
    private void OnGUI()
    {
        const float width = 260f;
        float x = Screen.width - width - 10f;
        const float top = 130f;

        if (collapsed)
        {
            if (GUI.Button(new Rect(Screen.width - 150f, top, 140f, 30f), "ライト確認 ▼")) collapsed = false;
            return;
        }

        // 画面が小さくても下まで操作できるように、はみ出す分はスクロールにする
        GUILayout.BeginArea(new Rect(x, top, width, Mathf.Min(520f, Screen.height - top - 10f)), GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("待機所: スポットライト");
        if (GUILayout.Button("たたむ ▲", GUILayout.Width(80))) collapsed = true;
        GUILayout.EndHorizontal();

        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("全部の台座で点ける")) ShowPreviewLights();
        if (GUILayout.Button("消す")) HidePreviewLights();
        GUILayout.EndHorizontal();

        // ---- 形 ----
        GUILayout.Space(8);
        GUILayout.Label($"根元の直径: {bottomDiameter:0.00} m");
        bottomDiameter = GUILayout.HorizontalSlider(bottomDiameter, 0.2f, 1.6f);
        GUILayout.Label($"広がる角度: {spreadAngle:0.0} 度");
        spreadAngle = GUILayout.HorizontalSlider(spreadAngle, 0f, 40f);
        GUILayout.Label($"高さ: {height:0.00} m");
        height = GUILayout.HorizontalSlider(height, 0.3f, 4f);
        GUILayout.Label($"根元の濃さ: {bottomAlpha:0.00}");
        bottomAlpha = GUILayout.HorizontalSlider(bottomAlpha, 0f, 1f);

        // ---- 色 ----
        GUILayout.Space(8);
        colorMode = (PlayerSpotlight.ColorMode)GUILayout.Toolbar((int)colorMode, ColorModeLabels);

        if (colorMode == PlayerSpotlight.ColorMode.SameForAll)
        {
            sameColor = ColorSliders("全員の色", sameColor);
        }
        else
        {
            // どの枠の色を変えるかを選んでから、その色をスライダーで変える
            editingSlot = GUILayout.Toolbar(editingSlot, SlotLabels);
            playerColors[editingSlot] = ColorSliders($"{SlotLabels[editingSlot]} の色", playerColors[editingSlot]);
        }

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("元に戻す"))
        {
            LoadFromPrefab();
            saveMessage = "";
        }
#if UNITY_EDITOR
        if (GUILayout.Button("プレハブに保存")) SaveToPrefab();
#endif
        GUILayout.EndHorizontal();
        if (saveMessage != "") GUILayout.Label(saveMessage);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    /// <summary>色の見本と、R/G/B の3本のスライダーを出す。変えた後の色を返す。</summary>
    private static Color ColorSliders(string label, Color color)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label);

        // 色の見本(白い四角を、その色で塗って描く)
        Rect swatch = GUILayoutUtility.GetRect(40f, 18f, GUILayout.Width(40f));
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(swatch, Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUILayout.EndHorizontal();

        color.r = ChannelSlider("R", color.r);
        color.g = ChannelSlider("G", color.g);
        color.b = ChannelSlider("B", color.b);
        color.a = 1f;
        return color;
    }

    // 色の1要素(0〜1)のスライダー。数値は0〜255で表示する(ペイントソフトと同じ見方)
    private static float ChannelSlider(string name, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{name} {Mathf.RoundToInt(value * 255f),3}", GUILayout.Width(50f));
        value = GUILayout.HorizontalSlider(value, 0f, 1f);
        GUILayout.EndHorizontal();
        return value;
    }

#if UNITY_EDITOR
    /// <summary>今の形と色を、光の柱のプレハブに書き込む(Play を止めても残る)</summary>
    private void SaveToPrefab()
    {
        if (joinSpotlight == null || joinSpotlight.SpotlightPrefab == null) return;

        string path = UnityEditor.AssetDatabase.GetAssetPath(joinSpotlight.SpotlightPrefab);
        if (string.IsNullOrEmpty(path)) return;

        GameObject root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
        try
        {
            // private な数値なので、インスペクターと同じ仕組み(SerializedObject)で書き込む
            var serialized = new UnityEditor.SerializedObject(root.GetComponent<PlayerSpotlight>());
            serialized.FindProperty("bottomDiameter").floatValue = bottomDiameter;
            serialized.FindProperty("spreadAngle").floatValue = spreadAngle;
            serialized.FindProperty("height").floatValue = height;
            serialized.FindProperty("bottomAlpha").floatValue = bottomAlpha;
            serialized.FindProperty("colorMode").enumValueIndex = (int)colorMode;
            serialized.FindProperty("sameColor").colorValue = sameColor;

            UnityEditor.SerializedProperty colors = serialized.FindProperty("playerColors");
            for (int i = 0; i < colors.arraySize && i < playerColors.Length; i++)
            {
                colors.GetArrayElementAtIndex(i).colorValue = playerColors[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            UnityEditor.PrefabUtility.UnloadPrefabContents(root);
        }
        saveMessage = "形と色をプレハブに保存しました";
    }
#endif
}
