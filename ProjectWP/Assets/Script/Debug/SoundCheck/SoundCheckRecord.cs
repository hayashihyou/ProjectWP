using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発用】サウンド確認シーンのレコード盤。音が鳴っている間だけ回る。
/// 絵は画像ファイルを用意せず、ここで作る。
///
///   ・盤: 細かい溝、曲と曲の間の少し太い溝、ふちの光る部分、中央のラベル
///   ・光の反射(Sheen): 盤とは別の画像にして「回さない」。本物のレコードと同じく、
///     盤が回っても光の位置は動かないので、溝の上を光が流れているように見える
///   ・ラベルの文字: 今鳴っている音の名前。盤と一緒に回る
///
/// 画像に差し替えたいときは、RawImage の Texture に画像を入れて「Generate Texture」を OFF にする。
/// </summary>
[RequireComponent(typeof(RawImage))]
public class SoundCheckRecord : MonoBehaviour
{
    [Tooltip("ON なら起動時にレコード盤の絵を作る")]
    [SerializeField] private bool _generateTexture = true;

    [Tooltip("光の反射を描く RawImage(盤の子にはしない = 回さない)")]
    [SerializeField] private RawImage _sheen;

    [Tooltip("ラベルに出す、今鳴っている音の名前(盤の子にする = 一緒に回る)")]
    [SerializeField] private TMP_Text _labelText;

    [Tooltip("回る速さ(1秒に何度)。本物のレコード(33回転)だと約200")]
    [SerializeField] private float _degreesPerSecond = 120f;

    [Tooltip("回り始め・止まるまでにかかる秒数")]
    [SerializeField] private float _spinUpSeconds = 0.8f;

    [Header("ラベルの色(左 → 右のグラデーション)")]
    [SerializeField] private Color _labelColorA = new Color(1f, 0.3f, 0.55f);
    [SerializeField] private Color _labelColorB = new Color(0.45f, 0.2f, 0.85f);

    private const int TextureSize = 1024;

    private float _speed; // 今の回る速さ(0 〜 _degreesPerSecond)

    private void Awake()
    {
        if (_generateTexture)
        {
            GetComponent<RawImage>().texture = CreateDiscTexture();
            if (_sheen != null) { _sheen.texture = CreateSheenTexture(); }
        }
    }

    private void Update()
    {
        // いきなり回る・止まるのではなく、少しずつ速く・遅くする
        float target = SoundCheckController.IsAnythingPlaying ? _degreesPerSecond : 0f;
        float accel = _degreesPerSecond / Mathf.Max(0.01f, _spinUpSeconds);
        _speed = Mathf.MoveTowards(_speed, target, accel * Time.unscaledDeltaTime);

        // 時計回りに回す(Z軸のマイナス方向)
        transform.Rotate(0f, 0f, -_speed * Time.unscaledDeltaTime);

        if (_labelText != null)
        {
            string name = SoundCheckController.NowPlayingName;
            _labelText.text = string.IsNullOrEmpty(name) ? "NO SIGNAL" : name;
        }
    }

    // ---------- 絵を作る(1ピクセルずつ「中心からの距離 r」と「角度」で色を決める) ----------

    private Texture2D CreateDiscTexture()
    {
        var pixels = new Color[TextureSize * TextureSize];
        float half = TextureSize * 0.5f;

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / half; // 0(中心) 〜 1(ふち)
                float angle = Mathf.Atan2(dy, dx);

                Color c;
                if (r < 0.022f)
                {
                    c = new Color(0.02f, 0.02f, 0.03f);                         // 真ん中の穴
                }
                else if (r < 0.03f)
                {
                    c = new Color(0.75f, 0.75f, 0.8f);                          // 穴のまわりの金属っぽい輪
                }
                else if (r < 0.34f)
                {
                    // ラベル: 左右方向のグラデーション(回ると色の向きが変わるので、回っているのが分かる)
                    float t = Mathf.Clamp01(dx / (half * 0.34f) * 0.5f + 0.5f);
                    c = Color.Lerp(_labelColorA, _labelColorB, t);
                    if (Mathf.Abs(r - 0.315f) < 0.003f) { c *= 0.7f; }          // ラベルのふちの細い線
                    c.a = 1f;
                }
                else if (r < 0.37f)
                {
                    float g = 0.035f;                                           // ラベルのまわりの溝のない部分
                    c = new Color(g, g, g * 1.1f);
                }
                else if (r < 0.975f)
                {
                    c = GrooveColor(r, angle);
                }
                else
                {
                    float g = 0.11f - (r - 0.975f) * 2f;                         // ふち(少し明るい)
                    c = new Color(g, g, g * 1.1f);
                }

                // ふちをなめらかにする(ギザギザ防止)
                c.a = Mathf.Clamp01((1f - r) * half * 0.9f);
                pixels[y * TextureSize + x] = c;
            }
        }

        return MakeTexture(pixels, "SoundCheckDisc");
    }

    // 溝の部分の色
    private static Color GrooveColor(float r, float angle)
    {
        float g = 0.05f;

        // 細かい溝(細い明暗のしま)。細かくしすぎると、ピクセルの並びと干渉して
        // 羽根のような模様(モアレ)が出るので、しま1本が数ピクセルになる細かさにしている
        g += 0.008f * Mathf.Sin(r * 360f);

        // 曲と曲の間の、溝のない少し太い輪
        float[] gaps = { 0.5f, 0.63f, 0.78f, 0.9f };
        foreach (float gap in gaps)
        {
            if (Mathf.Abs(r - gap) < 0.005f) { g = 0.02f; }
        }

        // 場所によって少しだけ明るさを変える(回っているのがうっすら分かるように)
        g *= 1f + 0.1f * Mathf.Sin(angle * 2f + r * 30f);

        return new Color(g, g, g * 1.12f, 1f);
    }

    // 光の反射。斜め上と斜め下に、細長い光が2本
    private Texture2D CreateSheenTexture()
    {
        const int size = 512;
        var pixels = new Color[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / half;
                float angle = Mathf.Atan2(dy, dx);

                // 溝の部分だけに光を乗せる
                float mask = Mathf.Clamp01((r - 0.37f) * 30f) * Mathf.Clamp01((0.975f - r) * 30f);

                // 細く・控えめに(強すぎると羽根のような模様に見えてしまう)
                float main = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle - 0.9f)), 700f) * 0.09f;
                float sub = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle + 0.7f)), 1200f) * 0.04f;

                pixels[y * size + x] = new Color(0.85f, 0.85f, 1f, (main + sub) * mask);
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static Texture2D MakeTexture(Color[] pixels, string name)
    {
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
            name = name,
        };
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
