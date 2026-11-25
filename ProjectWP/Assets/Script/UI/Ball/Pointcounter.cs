using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 上矢印キーで数字が+1され、文字だけがアニメーションする仮スクリプト。
/// TextMeshPro(UI) の Text にアタッチして使います。
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class PointCounter : MonoBehaviour
{
    [HideInInspector] public int count = 0;

    // ---- 跳ねるアニメーション ----
    [HideInInspector] public bool useBounce = true;
    [HideInInspector] public float bounceHeight = 30f;    // 上がる高さ(px)
    [HideInInspector] public float bounceDuration = 0.2f; // 上がって戻るまでの時間(秒)

    // ---- 拡大縮小アニメーション ----
    [HideInInspector] public bool useScale = true;
    [HideInInspector] public float scaleAmount = 1.5f;    // 最大倍率
    [HideInInspector] public float scaleDuration = 0.2f;  // 拡大して戻るまでの時間(秒)

    TextMeshProUGUI _text;
    RectTransform _rect;
    Vector2 _basePos;
    Vector3 _baseScale;

    float _bounceTimer = -1f; // -1 = 再生していない
    float _scaleTimer = -1f;

    void Awake()
    {
        _text = GetComponent<TextMeshProUGUI>();
        _rect = GetComponent<RectTransform>();
        _basePos = _rect.anchoredPosition;
        _baseScale = _rect.localScale;
        UpdateText();
    }

    void Update()
    {
        if (UpArrowPressed())
        {
            count++;
            UpdateText();
            if (useBounce) _bounceTimer = 0f;
            if (useScale) _scaleTimer = 0f;
        }

        // 両方チェック時も、それぞれ独立して同時に再生される
        Vector2 pos = _basePos;
        Vector3 scale = _baseScale;

        if (_bounceTimer >= 0f)
        {
            _bounceTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_bounceTimer / Mathf.Max(0.0001f, bounceDuration));
            pos.y += Mathf.Sin(t * Mathf.PI) * bounceHeight; // 0→上→0
            if (t >= 1f) _bounceTimer = -1f;
        }

        if (_scaleTimer >= 0f)
        {
            _scaleTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_scaleTimer / Mathf.Max(0.0001f, scaleDuration));
            float s = 1f + (scaleAmount - 1f) * Mathf.Sin(t * Mathf.PI); // 1→最大→1
            scale = _baseScale * s;
            if (t >= 1f) _scaleTimer = -1f;
        }

        _rect.anchoredPosition = pos;
        _rect.localScale = scale;
    }

    bool UpArrowPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.upArrowKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.UpArrow);
#endif
    }

    void UpdateText()
    {
        _text.text = count.ToString();
    }
}

#if UNITY_EDITOR
// チェックを入れたアニメーションの調整項目だけを表示するインスペクター
[CustomEditor(typeof(PointCounter))]
public class PointCounterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var c = (PointCounter)target;
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("現在の数字", c.count.ToString());
        EditorGUILayout.Space();

        c.useBounce = EditorGUILayout.ToggleLeft("跳ねるアニメーション", c.useBounce);
        if (c.useBounce)
        {
            EditorGUI.indentLevel++;
            c.bounceHeight = EditorGUILayout.FloatField("上がる高さ (px)", c.bounceHeight);
            c.bounceDuration = EditorGUILayout.FloatField("所要時間 (秒)", c.bounceDuration);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        c.useScale = EditorGUILayout.ToggleLeft("拡大縮小アニメーション", c.useScale);
        if (c.useScale)
        {
            EditorGUI.indentLevel++;
            c.scaleAmount = EditorGUILayout.FloatField("拡大倍率", c.scaleAmount);
            c.scaleDuration = EditorGUILayout.FloatField("拡縮にかける時間 (秒)", c.scaleDuration);
            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(c);
        }
    }
}
#endif