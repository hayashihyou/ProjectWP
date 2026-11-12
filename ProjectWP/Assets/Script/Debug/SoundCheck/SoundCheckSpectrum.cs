using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発用・お試し】レコード盤のまわりに、音に合わせて伸び縮みする細い線を円く並べる。
///
/// AudioListener.GetSpectrumData で「今スピーカーから出ている音(全部まとめて)」を
/// 低い音〜高い音に分けて取り出し、その大きさを線の長さにしている。
/// 音量スライダーを下げると線も短くなる(実際に聞こえる大きさで動く)。
///
/// レコード盤は画面の左にはみ出しているので、見えている右側(3時の方向)を低音にし、
/// 上下に離れるほど高い音になるよう鏡写しに並べている。
///
/// 使わないことにしたら、このコンポーネントの付いたオブジェクトを消すだけでよい。
/// </summary>
public class SoundCheckSpectrum : MonoBehaviour
{
    /// <summary>今の低音の大きさ(0〜1)。背景の光を揺らすのに使う</summary>
    public static float Bass { get; private set; }

    [Tooltip("線の本数(偶数)")]
    [SerializeField] private int _barCount = 140;

    [Tooltip("線を並べる円の半径(レコード盤の半径より少し大きくする)")]
    [SerializeField] private float _radius = 540f;

    [SerializeField] private float _barWidth = 5f;
    [Tooltip("音が鳴っていないときの線の長さ")]
    [SerializeField] private float _minLength = 3f;
    [Tooltip("いちばん大きい音のときの線の長さ")]
    [SerializeField] private float _maxLength = 140f;

    [Tooltip("この大きさ(dB)より小さい音は 0 として扱う")]
    [SerializeField] private float _floorDb = -72f;

    [Tooltip("線が伸びるときの速さ / 縮むときの速さ(大きいほど速い)")]
    [SerializeField] private float _riseSpeed = 30f;
    [SerializeField] private float _fallSpeed = 7f;

    [Tooltip("低音側 → 高音側 の色")]
    [SerializeField] private Gradient _colors = DefaultGradient();

    // 取り出す周波数の細かさ(2の累乗。大きいほど細かいが重い)
    private const int SampleCount = 1024;

    private readonly float[] _spectrum = new float[SampleCount];
    private RectTransform[] _bars;
    private Image[] _images;
    private float[] _levels; // 片側の線ごとの今の長さ(0〜1)

    private void Start()
    {
        int half = _barCount / 2;
        _bars = new RectTransform[half * 2];
        _images = new Image[half * 2];
        _levels = new float[half];

        for (int i = 0; i < _bars.Length; i++)
        {
            var go = new GameObject($"Bar {i}", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);

            // 下端を円の上に置き、外向きに伸びるようにする(0本目 = 3時の方向、反時計回り)
            rt.pivot = new Vector2(0.5f, 0f);
            float angle = 360f * i / _bars.Length;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            rt.anchoredPosition = rt.localRotation * Vector3.up * _radius;
            rt.sizeDelta = new Vector2(_barWidth, _minLength);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;

            _bars[i] = rt;
            _images[i] = image;
        }
    }

    private void Update()
    {
        AudioListener.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        int half = _levels.Length;
        float dt = Time.unscaledDeltaTime;

        for (int b = 0; b < half; b++)
        {
            float target = BandLevel(b, half);
            float speed = target > _levels[b] ? _riseSpeed : _fallSpeed;
            _levels[b] = Mathf.Lerp(_levels[b], target, 1f - Mathf.Exp(-speed * dt));
        }

        // いちばん低い数本の平均を「低音の大きさ」とする
        float bass = 0f;
        int bassBands = Mathf.Max(1, half / 10);
        for (int b = 0; b < bassBands; b++) { bass += _levels[b]; }
        Bass = bass / bassBands;

        for (int i = 0; i < _bars.Length; i++)
        {
            int band = BandOf(i, half);
            float level = _levels[band];
            _bars[i].sizeDelta = new Vector2(_barWidth, Mathf.Lerp(_minLength, _maxLength, level));

            // 色は高さで決め、静かなときは薄くする
            Color c = _colors.Evaluate((float)band / (half - 1));
            c.a = Mathf.Lerp(0.18f, 1f, level);
            _images[i].color = c;
        }
    }

    // i 本目の線が何番目の高さ(0=低音)を受け持つか。3時の方向から上下に鏡写し
    private int BandOf(int i, int half)
    {
        int band = i <= half ? i : _bars.Length - i;
        return Mathf.Min(band, half - 1);
    }

    // b 番目が受け持つ周波数の範囲の大きさを 0〜1 で返す。
    // 人の耳に合わせて、低い音ほど細かく・高い音ほどまとめて受け持つ(対数の間隔)
    private float BandLevel(int b, int bandCount)
    {
        const float minIndex = 2f; // いちばん低い数個は直流成分などなので飛ばす
        float maxIndex = SampleCount * 0.5f; // 上半分は耳にほぼ聞こえない高さなので使わない

        int from = Mathf.FloorToInt(minIndex * Mathf.Pow(maxIndex / minIndex, (float)b / bandCount));
        int to = Mathf.FloorToInt(minIndex * Mathf.Pow(maxIndex / minIndex, (float)(b + 1) / bandCount));
        to = Mathf.Max(to, from + 1);

        float peak = 0f;
        for (int i = from; i < to && i < SampleCount; i++)
        {
            peak = Mathf.Max(peak, _spectrum[i]);
        }

        // 大きさを dB にして 0〜1 にそろえる(そのままだと小さい音がほとんど見えないため)
        float db = 20f * Mathf.Log10(Mathf.Max(peak, 1e-7f));
        return Mathf.Clamp01((db - _floorDb) / -_floorDb);
    }

    private void OnDisable()
    {
        Bass = 0f;
    }

    private static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.25f, 0.88f, 0.82f), 0f),
                new GradientColorKey(new Color(0.45f, 0.45f, 1f), 0.45f),
                new GradientColorKey(new Color(1f, 0.3f, 0.55f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
