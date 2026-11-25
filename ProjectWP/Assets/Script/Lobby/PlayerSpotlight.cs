using UnityEngine;

/// <summary>
/// 待機所の台座から上に向けて当てる、白いスポットライト(光の柱)1本分の見た目。
/// プレハブ(Prefabs/Effects/PlayerJoinSpotlight)のルートに付けて使う。
///
/// 光の柱は、台座から上へ伸びる半透明の筒。懐中電灯の光のように、上に行くほど外側へ広がり、透明になって消える。
/// 筒の形はこのスクリプトが作るので、「根元の直径」「広がる角度」「高さ」「濃さ」の数値だけで見た目を変えられる。
/// 色は「全員同じ色」か「プレイヤー(枠)ごとの色」を選べる。
///   ・インスペクターで数値を変えると、Play中でもその場で形と色が変わる。
///   ・コードから変えたいときは SetShape / SetColors を呼ぶ(テスト用パネル PlayerSpotlightTester が使っている)。
///
/// このオブジェクトの位置 = 台座の上面の中心。ここから上に向かって光る。
/// ネットワーク同期はしない(各PCが自分の画面にだけ出す)。
/// </summary>
public class PlayerSpotlight : MonoBehaviour
{
    [Header("光の柱の形")]
    [Tooltip("光の柱を描くMeshRenderer(子の Beam)")]
    [SerializeField] private MeshRenderer beamRenderer;

    [Tooltip("根元(台座の上)の直径(m)。台座の上面(約1.6m)より少し小さくする。")]
    [SerializeField] private float bottomDiameter = 0.9f;

    [Tooltip("外側へ広がる角度(度)。0でまっすぐな筒、大きいほど上が広がる。")]
    [Range(0f, 45f)]
    [SerializeField] private float spreadAngle = 16f;

    [Tooltip("柱の高さ(m)。キャラクターの身長より少し高くする。ここで光が消え切る。")]
    [SerializeField] private float height = 2.6f;

    [Tooltip("根元の濃さ(0=透明, 1=真っ白)。上に行くほど0に近づく。")]
    [Range(0f, 1f)]
    [SerializeField] private float bottomAlpha = 0.35f;

    [Tooltip("柱のまわりの分割数。多いほど丸くなる。")]
    [SerializeField] private int segments = 32;

    /// <summary>光の色の決め方</summary>
    public enum ColorMode
    {
        SameForAll,  // 全員同じ色(sameColor)
        PerPlayer,   // 枠ごとの色(playerColors)
    }

    [Header("色")]
    [Tooltip("全員同じ色にするか、プレイヤー(枠)ごとの色にするか")]
    [SerializeField] private ColorMode colorMode = ColorMode.SameForAll;

    [Tooltip("全員同じ色にするときの色")]
    [SerializeField] private Color sameColor = Color.white;

    [Tooltip("プレイヤーごとの色にするときの色。先頭から1P, 2P, 3P, 4Pの順。")]
    [SerializeField]
    private Color[] playerColors =
    {
        Color.white,                    // 1P: 白
        new Color(0.75f, 0.6f, 1f),     // 2P: 明るい紫
        new Color(0.4f, 0.9f, 1f),      // 3P: 水色
        new Color(0.6f, 1f, 0.2f),      // 4P: 黄緑
    };

    [Header("点く・消える")]
    [Tooltip("点くとき・消えるときに、ふわっと変化する秒数")]
    [SerializeField] private float fadeSeconds = 0.35f;

    // シェーダーの色のプロパティ(URPのParticles/Unlitは _BaseColor)
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Mesh beamMesh;
    private MaterialPropertyBlock propertyBlock;
    private float materialAlpha = 1f;  // マテリアルで決めた透明度(色を変えてもこれは残す)

    private int slotIndex = -1;   // 照らしている枠(0=1P, ...)。-1は未設定(1Pの色を使う)

    private float currentLevel;   // 今の明るさ(0=消えている, 1=点いている)
    private float targetLevel;    // 目指している明るさ
    private bool destroyWhenOff;  // 消え切ったらこのオブジェクトを消すか

    public float BottomDiameter => bottomDiameter;
    public float SpreadAngle => spreadAngle;
    public float Height => height;
    public float BottomAlpha => bottomAlpha;
    public ColorMode CurrentColorMode => colorMode;
    public Color SameColor => sameColor;

    /// <summary>プレイヤーごとの色(枠の番号の色)。範囲外なら白</summary>
    public Color GetPlayerColor(int index)
    {
        return index >= 0 && index < playerColors.Length ? playerColors[index] : Color.white;
    }

    private void Awake()
    {
        // マテリアルの透明度を覚えておき、色と濃さだけ変えて上書きする。
        // マテリアル自体を書き換えると全部の柱に影響するので、PropertyBlockで1本ずつ変える。
        propertyBlock = new MaterialPropertyBlock();
        materialAlpha = beamRenderer.sharedMaterial.GetColor(BaseColorId).a;

        RebuildMesh();

        // 最初は消えた状態から始める
        ApplyLevel(0f);
    }

    /// <summary>
    /// 光の柱の形をまとめて変える(すぐに見た目に反映される)。
    /// </summary>
    /// <param name="newBottomDiameter">根元の直径(m)</param>
    /// <param name="newSpreadAngle">外側へ広がる角度(度)</param>
    /// <param name="newHeight">高さ(m)</param>
    /// <param name="newBottomAlpha">根元の濃さ(0〜1)</param>
    public void SetShape(float newBottomDiameter, float newSpreadAngle, float newHeight, float newBottomAlpha)
    {
        bottomDiameter = newBottomDiameter;
        spreadAngle = newSpreadAngle;
        height = newHeight;
        bottomAlpha = newBottomAlpha;
        RebuildMesh();
    }

    /// <summary>どの枠(0=1P, ...)を照らしているかを教える。「プレイヤーごとの色」のときに、その枠の色になる。</summary>
    public void SetSlotIndex(int index)
    {
        slotIndex = index;
        ApplyLevel(currentLevel);
    }

    /// <summary>
    /// 色の決め方をまとめて変える(すぐに見た目に反映される)。
    /// </summary>
    /// <param name="newMode">全員同じ色か、プレイヤーごとの色か</param>
    /// <param name="newSameColor">全員同じ色にするときの色</param>
    /// <param name="newPlayerColors">プレイヤーごとの色(1P, 2P, ...の順)</param>
    public void SetColors(ColorMode newMode, Color newSameColor, Color[] newPlayerColors)
    {
        colorMode = newMode;
        sameColor = newSameColor;
        for (int i = 0; i < playerColors.Length && i < newPlayerColors.Length; i++)
        {
            playerColors[i] = newPlayerColors[i];
        }
        ApplyLevel(currentLevel);
    }

    // 今の設定で、この柱の色を決める
    private Color GetCurrentColor()
    {
        if (colorMode == ColorMode.PerPlayer)
        {
            // 枠がまだ分からないときは1Pの色にしておく
            return GetPlayerColor(Mathf.Max(slotIndex, 0));
        }
        return sameColor;
    }

    /// <summary>ふわっと点ける</summary>
    public void TurnOn()
    {
        targetLevel = 1f;
        destroyWhenOff = false;
    }

    /// <summary>ふわっと消す。消え切ったらこのオブジェクトごと消える。</summary>
    public void TurnOffAndDestroy()
    {
        targetLevel = 0f;
        destroyWhenOff = true;
    }

    /// <summary>すぐに消した状態にする(もう一度点ける前に「いない状態」を見せたいとき用)</summary>
    public void HideImmediately()
    {
        targetLevel = 0f;
        currentLevel = 0f;
        ApplyLevel(0f);
    }

    private void Update()
    {
        if (!Mathf.Approximately(currentLevel, targetLevel))
        {
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            currentLevel = Mathf.MoveTowards(currentLevel, targetLevel, step);
            ApplyLevel(currentLevel);
        }

        if (destroyWhenOff && currentLevel <= 0f)
        {
            Destroy(gameObject);
        }
    }

    // Play中にインスペクターで数値を変えたら、その場で形と色を作り直す
    private void OnValidate()
    {
        if (Application.isPlaying && beamMesh != null)
        {
            RebuildMesh();
            ApplyLevel(currentLevel);
        }
    }

    // 色と明るさ(0〜1)を、柱に反映する
    private void ApplyLevel(float level)
    {
        Color color = GetCurrentColor();
        color.a = materialAlpha * level;
        beamRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        beamRenderer.SetPropertyBlock(propertyBlock);

        // 完全に消えているときは描画しない(無駄な描画を減らす)
        beamRenderer.enabled = level > 0f;
    }

    /// <summary>
    /// 今の数値で、光の柱の形(ふたのない円すい台)を作り直す。
    /// 頂点の色の透明度を、下 = bottomAlpha、上 = 0 にしておくことで、上に行くほど消えていくように見せる。
    /// (シェーダーが頂点の色を掛け算してくれるので、グラデーションの画像を用意しなくてよい)
    /// </summary>
    private void RebuildMesh()
    {
        if (beamMesh == null)
        {
            beamMesh = new Mesh { name = "SpotlightBeam" };
            beamRenderer.GetComponent<MeshFilter>().sharedMesh = beamMesh;
        }

        int count = Mathf.Max(segments, 3);
        float bottomRadius = bottomDiameter * 0.5f;
        // 広がる角度から、てっぺんの半径を決める(高さ × tan(角度) の分だけ外へ広がる)
        float topRadius = bottomRadius + height * Mathf.Tan(spreadAngle * Mathf.Deg2Rad);

        // 縦方向の分割数。透明度は縦にまっすぐ変わるだけなので、上下2段で足りる
        Vector3[] vertices = new Vector3[(count + 1) * 2];
        Color[] colors = new Color[vertices.Length];
        int[] triangles = new int[count * 6];

        Color bottomColor = new Color(1f, 1f, 1f, bottomAlpha);
        Color topColor = new Color(1f, 1f, 1f, 0f);

        for (int i = 0; i <= count; i++)
        {
            float angle = (float)i / count * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            vertices[i * 2] = new Vector3(cos * bottomRadius, 0f, sin * bottomRadius);    // 下の輪(根元)
            vertices[i * 2 + 1] = new Vector3(cos * topRadius, height, sin * topRadius);  // 上の輪(広がった先)
            colors[i * 2] = bottomColor;
            colors[i * 2 + 1] = topColor;
        }

        for (int i = 0; i < count; i++)
        {
            int bottom = i * 2;
            int t = i * 6;
            triangles[t] = bottom;
            triangles[t + 1] = bottom + 1;
            triangles[t + 2] = bottom + 2;
            triangles[t + 3] = bottom + 2;
            triangles[t + 4] = bottom + 1;
            triangles[t + 5] = bottom + 3;
        }

        // 頂点の数が変わることがあるので、先に三角形を消してから入れ直す
        beamMesh.Clear();
        beamMesh.vertices = vertices;
        beamMesh.colors = colors;
        beamMesh.triangles = triangles;
        beamMesh.RecalculateNormals();
        beamMesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
        // 自分で作った形はアセットではないので、自分で消しておく
        if (beamMesh != null)
        {
            Destroy(beamMesh);
        }
    }
}
