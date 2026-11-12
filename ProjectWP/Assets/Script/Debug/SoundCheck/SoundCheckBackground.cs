using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発用】サウンド確認シーンの背景。画像ファイルを使わずに、起動時に絵を作る。
///   ・上から下への暗いグラデーション
///   ・レコード盤のうしろのぼんやりした光(低音に合わせて明るさが揺れる)
///   ・画面のふちを暗くする(視線が真ん中に集まるように)
/// </summary>
[RequireComponent(typeof(RawImage))]
public class SoundCheckBackground : MonoBehaviour
{
    [SerializeField] private Color _topColor = new Color(0.07f, 0.06f, 0.13f);
    [SerializeField] private Color _bottomColor = new Color(0.02f, 0.02f, 0.05f);

    [Header("レコード盤のうしろの光")]
    [Tooltip("光の中心(画面の左下が 0,0 / 右上が 1,1)")]
    [SerializeField] private Vector2 _glowCenter = new Vector2(0.12f, 0.45f);
    [SerializeField] private float _glowRadius = 0.55f;
    [SerializeField] private Color _glowColor = new Color(0.55f, 0.2f, 0.85f);

    [Tooltip("低音に合わせて光を明るくする強さ")]
    [SerializeField] private float _bassPulse = 0.6f;

    [Tooltip("光だけを描く RawImage(低音に合わせて明るさを変えるため、背景とは別にしている)")]
    [SerializeField] private RawImage _glowImage;

    private const int Width = 256;
    private const int Height = 144;

    private void Awake()
    {
        GetComponent<RawImage>().texture = CreateBaseTexture();
        if (_glowImage != null) { _glowImage.texture = CreateGlowTexture(); }
    }

    private void Update()
    {
        if (_glowImage == null) { return; }
        Color c = Color.white;
        c.a = Mathf.Clamp01(0.55f + SoundCheckSpectrum.Bass * _bassPulse);
        _glowImage.color = c;
    }

    // グラデーション + ふちを暗く
    private Texture2D CreateBaseTexture()
    {
        var pixels = new Color[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float u = (float)x / (Width - 1);
                float v = (float)y / (Height - 1);
                Color c = Color.Lerp(_bottomColor, _topColor, v);

                float dx = (u - 0.5f) * 1.6f;
                float dy = (v - 0.5f) * 1.6f;
                float vignette = 1f - Mathf.Clamp01(dx * dx + dy * dy) * 0.55f;
                c *= vignette;
                c.a = 1f;
                pixels[y * Width + x] = c;
            }
        }
        return MakeTexture(pixels);
    }

    // 光(中心が明るく、外に向かってなめらかに消える)
    private Texture2D CreateGlowTexture()
    {
        var pixels = new Color[Width * Height];
        float aspect = (float)Width / Height;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float dx = ((float)x / (Width - 1) - _glowCenter.x) * aspect;
                float dy = (float)y / (Height - 1) - _glowCenter.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / _glowRadius;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f) * 0.7f;

                Color c = _glowColor;
                c.a = a;
                pixels[y * Width + x] = c;
            }
        }
        return MakeTexture(pixels);
    }

    private static Texture2D MakeTexture(Color[] pixels)
    {
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
