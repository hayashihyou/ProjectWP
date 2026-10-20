using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// TextMeshPro の文字を波打たせる汎用アニメーション。
/// どの TMP_Text(TextMeshProUGUI / TextMeshPro)にもアタッチして使えます。
/// UniTask で非同期ループ駆動するため、Time.timeScale = 0 や読み込み中でも動かせます。
/// ※重い同期処理でメインスレッドが止まる場合は UniTask / async API 側で非同期化してください。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class TMPTextWaveAnimator : MonoBehaviour
{
    [Header("再生設定")]
    [SerializeField] private bool playOnEnable = true;
    [Tooltip("true: Time.timeScale の影響を受けない(ロード画面向け)")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("ループ設定")]
    [Tooltip("true: アニメーション終了後、待ち時間を置いて最初から再生を繰り返す\nfalse: 1回再生して終了")]
    [SerializeField] private bool loop = true;
    [Tooltip("アニメーションが終わってから再度再生するまでの待ち時間(秒)。ループ有効時のみ使用")]
    [Min(0f)][SerializeField] private float loopDelay = 0.5f;

    [Header("対象範囲")]
    [Tooltip("none:全文字が対象")]
    [SerializeField] private bool useRange = false;
    [Tooltip("動かし始める文字(0始まり)。例: \"Now Loading...\" の \"...\" だけなら 12")]
    [Min(0)][SerializeField] private int startIndex = 0;
    [Tooltip("動かす最後の文字(含む)。例: \"Now Loading...\" の \"...\" だけなら 14")]
    [Min(0)][SerializeField] private int endIndex = 0;

    [Header("動きの細かい設定 (0 = なし)")]
    [Tooltip("動く大きさ(ローカル頂点単位)。プラスで上、マイナスで下に動く。0で上下移動なし")]
    [SerializeField] private float amplitude = 20f;
    [Tooltip("動く速さ。大きいほど速く、0で止まる。\n初期値:2")]
    [Min(0f)][SerializeField] private float speed = 2f;
    [Tooltip("隣の文字との動き出しのずれ(数値0~1)。0で全文字が同時に動き、大きいほど順番にずれて動く。\n初期値:0.2")]
    [Min(0f)][SerializeField] private float interval = 0.2f;
    [Tooltip("拡大の大きさ。一番持ち上がった時に(1+指定された数字)倍になる。0で拡大なし")]
    [Min(0f)][SerializeField] private float scaleAmount = 0f;

    private TMP_Text text;
    private CancellationTokenSource cts;
    private float elapsed;

    public bool IsPlaying => cts != null;

    // ----------------------------------------------------------------
    // Unity
    // ----------------------------------------------------------------
    // インスペクターで intervalを入力したとき 0~1 に収める(数値入力のまま上限だけ制限)
    private void OnValidate()
    {
        interval = Mathf.Clamp01(interval);
    }

    private void Awake()
    {
        text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        if (playOnEnable) Play();
    }

    private void OnDisable()
    {
        Stop(true);
    }

    private void OnDestroy()
    {
        Stop(false);
    }

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    /// <summary>初期状態に戻して最初から再生する(何度呼んでも毎回同じ動きから始まる)</summary>
    public void Play()
    {
        if (text == null) text = GetComponent<TMP_Text>();

        Stop(false);          // 既存ループを止める
        ResetAnimation();     // 経過時間を 0 に戻す
        cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        AnimateAsync(cts.Token).Forget();
    }

    /// <summary>アニメーションを停止する。restore=true で文字を元の位置・大きさに戻す</summary>
    public void Stop(bool restore = true)
    {
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }

        if (restore) RestoreText();
    }

    /// <summary>アニメーションの内部状態(経過時間)を初期化する</summary>
    public void ResetAnimation()
    {
        elapsed = 0f;
        RestoreText();
    }

    /// <summary>動かす範囲を実行中に変更する(両端含む)</summary>
    public void SetRange(int start, int end)
    {
        useRange = true;
        startIndex = Mathf.Max(0, start);
        endIndex = Mathf.Max(startIndex, end);
    }

    /// <summary>全文字を対象に戻す</summary>
    public void ClearRange() => useRange = false;

    /// <summary>ループ設定を実行中に変更する</summary>
    public void SetLoop(bool enableLoop, float delaySeconds)
    {
        loop = enableLoop;
        loopDelay = Mathf.Max(0f, delaySeconds);
    }

    // ----------------------------------------------------------------
    // Async loop (UniTask)
    // ----------------------------------------------------------------
    private async UniTaskVoid AnimateAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // ---- 再生フェーズ ----
                while (true)
                {
                    float duration = ApplyFrame(elapsed);   // この再生の長さ(秒)が返る
                    if (elapsed >= duration) break;         // 再生終了

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += GetDeltaTime();
                }

                // ---- 終了処理 ----
                if (!loop)
                {
                    if (!token.IsCancellationRequested) Stop(true); // 1回で終了
                    return;
                }

                // ---- 待機フェーズ(文字は元の状態で待つ) ----
                float waited = 0f;
                do
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    waited += GetDeltaTime();
                }
                while (waited < loopDelay);

                // ---- 初期化して最初の動きから再生 ----
                elapsed = 0f;
            }
        }
        catch (OperationCanceledException)
        {
            // Stop / Destroy による正常なキャンセル
        }
    }

    private float GetDeltaTime() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    // ----------------------------------------------------------------
    // Mesh manipulation
    // ----------------------------------------------------------------
    private void RestoreText()
    {
        if (text == null) return;
        text.ForceMeshUpdate(); // 元の頂点で再生成される
    }

    /// <summary>
    /// 指定時間のフレームを適用し、この再生の長さ(秒)を返す。
    /// speed が 0 の場合は終わらないので PositiveInfinity を返す。
    /// </summary>
    private float ApplyFrame(float time)
    {
        if (text == null) return 0f;

        // 元の頂点で再生成 → そこから毎フレーム加工する(累積ズレを防ぐ)
        text.ForceMeshUpdate();
        TMP_TextInfo textInfo = text.textInfo;

        int charCount = textInfo.characterCount;
        if (charCount == 0) return 0f;

        int first = useRange ? Mathf.Clamp(startIndex, 0, charCount - 1) : 0;
        int last = useRange ? Mathf.Clamp(endIndex, 0, charCount - 1) : charCount - 1;
        if (first > last) return 0f;

        int rangeCount = last - first + 1;

        for (int i = first; i <= last; i++)
        {
            TMP_CharacterInfo ci = textInfo.characterInfo[i];
            if (!ci.isVisible) continue; // スペース・改行など

            Evaluate(time, i - first, out float offsetY, out float scale);

            Vector3[] verts = textInfo.meshInfo[ci.materialReferenceIndex].vertices;
            int v = ci.vertexIndex;

            // 文字の中心を基準に拡大縮小(左下 v+0 と右上 v+2 の中点)
            Vector3 center = (verts[v] + verts[v + 2]) * 0.5f;
            Vector3 offset = new Vector3(0f, offsetY, 0f);

            for (int j = 0; j < 4; j++)
            {
                verts[v + j] = center + (verts[v + j] - center) * scale + offset;
            }
        }

        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);

        return GetPlayDuration(rangeCount);
    }

    /// <summary>1回の再生にかかる時間(秒)。最後の文字の動きが終わるまで</summary>
    private float GetPlayDuration(int rangeCount)
    {
        if (speed <= 0f) return float.PositiveInfinity;

        float totalUnits = (rangeCount - 1) * interval + 1f;
        return totalUnits / speed;
    }

    /// <summary>文字ごとの上下オフセットと拡大率を計算(自分の番が来る前・終わった後は静止)</summary>
    private void Evaluate(float time, int localIndex, out float offsetY, out float scale)
    {
        float u = time * speed;                     // 跳ね単位の時間
        float c = u - localIndex * interval;        // この文字にとっての経過(文字ごとの時間差を引く)
        float k = 0f;                               // 0..1 (0=元の位置, 1=一番持ち上がった位置)

        if (c >= 0f && c < 1f)
        {
            k = EaseBump(c);                        // 1回だけ跳ねる
        }

        offsetY = k * amplitude;
        scale = 1f + k * scaleAmount;
    }

    /// <summary>
    /// 0→1 の進行度を 0 → 1 → 0 の山に変換する(前半で上昇、後半で下降、各区間 ease-in-out)。
    /// CSS の「0%,100% {translateY(0)} 50% {translateY(-20px)} + ease-in-out」と同じ動き。
    /// </summary>
    private static float EaseBump(float p)
    {
        float t = p < 0.5f ? p * 2f : (1f - p) * 2f;
        return t * t * (3f - 2f * t);               // smoothstep ≒ ease-in-out
    }
}