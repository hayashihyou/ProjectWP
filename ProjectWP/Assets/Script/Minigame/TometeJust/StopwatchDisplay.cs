using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 机の上のストップウォッチ1台分(見た目と状態)。StopwatchDisplay プレハブの一番上に付ける。
///
/// このスクリプトがやること
/// ・今どの状態か(待機 → 計測中 → ストップ / 時間切れ → ラウンド終了)を覚えておく
/// ・ストップしたら、円形のリングが外側に広がりながら消えるエフェクトを出し、縁をプレイヤーの色で光らせる
/// ・状態が変わるたびに合図(イベント)を出す
///
/// このスクリプトがやらないこと(UI担当の範囲)
/// ・文字盤の文字(TimeText)を書き換える・アニメーションさせる
///   → UI担当は、下の「UI担当が読むもの」「UI担当が受け取る合図」だけを使って、別のスクリプトで表示を作る
///
/// 状態を変えるメソッド(PrepareRound / Stop など)は、StopwatchDeskDisplays が自動で呼ぶ。
/// ネットワークのことは StopwatchDeskDisplays と MinigameInfo が受け持つので、ここは各端末の見た目だけを扱う。
/// </summary>
public class StopwatchDisplay : MonoBehaviour
{
    /// <summary>ストップウォッチの状態</summary>
    public enum DisplayState
    {
        Waiting,    // 待機(ラウンドの開始前・カウントダウン中)
        Running,    // 計測中(プレイ中で、まだ押していない)
        Stopped,    // ストップした(縁が光っている)
        TimedOut,   // 押さないまま時間切れになった
        RoundOver,  // ラウンドが終わった(止めた時間を見せてよいタイミング)
        GameOver,   // 全ラウンドが終わった(合計の誤差を見せる)
    }

    [Header("どのプレイヤーの机か")]
    [Tooltip("枠番号(0 = 1P, 1 = 2P …)。-1 のままなら、親の机の名前(Desk_1P など)から自動で決める")]
    [SerializeField] private int slotIndex = -1;

    [Header("表示")]
    [Tooltip("時間を表示する文字。UI担当が書き換える。空なら子の TimeText を自動で探す")]
    [SerializeField] private TMP_Text timeText;

    [Tooltip("スタートしてから何秒間、進んでいる時間を見せるか(その後は隠す)。UI担当は ShouldShowRunningTime で判定できる")]
    [SerializeField] private float visibleSecondsAtStart = 3f;

    [Header("光の色")]
    [Tooltip("リングと縁の色。アルファが0のままなら、本体(Watch/Body)の色 = プレイヤーの色を使う")]
    [SerializeField] private Color glowColor = new Color(0f, 0f, 0f, 0f);

    [Header("広がるリング(押した瞬間のエフェクト)")]
    [Tooltip("広がるリング。空なら子の Watch/Ring を自動で探す(普段は非表示にしておく)。" +
             "リングの外側ほど薄くなるのは、マテリアル M_Stopwatch_Ring のテクスチャ(T_StopwatchRing_Gradient)で決まっている")]
    [SerializeField] private Transform ring;

    [Tooltip("リングが広がって消えるまでの秒数")]
    [SerializeField] private float ringDuration = 0.6f;

    [Tooltip("リングの最初の大きさ(倍率)。ストップウォッチの外周くらい")]
    [SerializeField] private float ringStartScale = 0.8f;

    [Tooltip("リングの最後の大きさ(倍率)")]
    [SerializeField] private float ringEndScale = 1.9f;

    [Tooltip("リングの最初の濃さ(0 = 透明、1 = くっきり)")]
    [SerializeField, Range(0f, 1f)] private float ringStartAlpha = 1f;

    [Tooltip("リングの明るさ。プレイヤーの色をどれだけ白に近づけるか(0 = プレイヤーの色そのまま、1 = 真っ白)")]
    [SerializeField, Range(0f, 1f)] private float ringBrightness = 0.35f;

    [Header("縁の光(押した後ずっと)")]
    [Tooltip("光らせる部品。空なら子の Watch/Bezel(文字盤の縁)を自動で探す")]
    [SerializeField] private Renderer[] glowRenderers;

    [Tooltip("押した後、光り続けるときの強さ")]
    [SerializeField] private float glowIntensity = 1.5f;

    [Tooltip("押した瞬間の強さ(リングが広がる間に glowIntensity まで弱まる)")]
    [SerializeField] private float flashIntensity = 4f;

    // 光らせる部品ごとのマテリアル(実行中だけ使う複製)。共有のマテリアルを直接変えると、他の机まで光ってしまうため
    private Material[] glowMaterials;

    // 光らせる前の元の色(消すときに戻すため)
    private Color[] originalColors;

    // リングのマテリアル(実行中だけ使う複製)
    private Material ringMaterial;

    // 今動いているエフェクト(新しく始めるとき・消すときに止めるため)
    private Coroutine effectRoutine;


    // ==================== UI担当が読むもの ====================

    /// <summary>枠番号(0 = 1P)</summary>
    public int SlotIndex => slotIndex;

    /// <summary>今の状態</summary>
    public DisplayState State { get; private set; } = DisplayState.Waiting;

    /// <summary>この机の枠にプレイヤーがいるか(2人で遊ぶときの 3P・4P などは false)</summary>
    public bool IsOccupied { get; private set; } = true;

    /// <summary>このラウンドで止めたか(ストップ または 時間切れ)</summary>
    public bool HasStopped => State == DisplayState.Stopped || State == DisplayState.TimedOut || (State == DisplayState.RoundOver && StoppedSeconds >= 0f);

    /// <summary>止めた秒数(ストップ または 時間切れの秒数)。まだ止めていなければ -1</summary>
    public float StoppedSeconds { get; private set; } = -1f;

    /// <summary>時間切れで止まったか(自分で押したなら false)</summary>
    public bool IsTimedOut { get; private set; }

    /// <summary>時間を表示する文字(UI担当はここに書き込む)</summary>
    public TMP_Text TimeText => timeText;

    /// <summary>進んでいる時間を見せてよいか(計測中で、スタートから visibleSecondsAtStart 秒以内なら true)</summary>
    public bool ShouldShowRunningTime
    {
        get
        {
            if (State != DisplayState.Running) { return false; }
            // スタートの合図(RPC)は、経過時間の同期より先に届くことがある。そのときは -1 になるが、
            // まだ始まったばかりなので「見せてよい」とする(false にすると、始まった瞬間に隠れてしまう)
            float elapsed = MinigameInfo.ElapsedTime;
            return elapsed < visibleSecondsAtStart;
        }
    }


    // ==================== UI担当が受け取る合図 ====================
    // どれも、全員の端末で1回ずつ呼ばれる。OnEnable で += 、OnDisable で -= する

    /// <summary>状態が変わった(このストップウォッチ, 新しい状態)。どの合図の前にも必ず呼ばれる</summary>
    public event Action<StopwatchDisplay, DisplayState> OnStateChanged;

    /// <summary>新しいラウンドの準備になった(待機に戻った)。表示を元に戻すきっかけ</summary>
    public event Action<StopwatchDisplay> OnRoundReady;

    /// <summary>計測が始まった。進む時間の表示を始めるきっかけ</summary>
    public event Action<StopwatchDisplay> OnRunningStarted;

    /// <summary>ストップした(このストップウォッチ, 止めた秒数)。「STOP」を出すきっかけ</summary>
    public event Action<StopwatchDisplay, float> OnStopped;

    /// <summary>押さないまま時間切れになった(このストップウォッチ, 時間切れの秒数)</summary>
    public event Action<StopwatchDisplay, float> OnTimedOut;

    /// <summary>ラウンドが終わった(このストップウォッチ, 止めた秒数。まだ届いていなければ -1)。止めた時間を見せるきっかけ</summary>
    public event Action<StopwatchDisplay, float> OnRoundOver;

    /// <summary>全ラウンドが終わった(このストップウォッチ, 合計の誤差)。合計の誤差を見せるきっかけ</summary>
    public event Action<StopwatchDisplay, float> OnGameOver;


    // ==================== 状態を変えるメソッド(StopwatchDeskDisplays が呼ぶ) ====================

    /// <summary>新しいラウンドの準備。光とエフェクトを消して待機に戻す</summary>
    public void PrepareRound(bool occupied)
    {
        IsOccupied = occupied;
        StoppedSeconds = -1f;
        IsTimedOut = false;
        StopEffect();
        SetEmission(0f, false);
        ChangeState(DisplayState.Waiting);
        OnRoundReady?.Invoke(this);
    }

    /// <summary>計測を始める(プレイが始まった)</summary>
    public void StartRunning()
    {
        ChangeState(DisplayState.Running);
        OnRunningStarted?.Invoke(this);
    }

    /// <summary>ストップした。リングを広げて、縁をプレイヤーの色で光らせる</summary>
    public void Stop(float seconds)
    {
        StoppedSeconds = seconds;
        IsTimedOut = false;
        StopEffect();
        effectRoutine = StartCoroutine(StopEffectRoutine());
        ChangeState(DisplayState.Stopped);
        OnStopped?.Invoke(this, seconds);
    }

    /// <summary>押さないまま時間切れになった。光らせない</summary>
    public void TimeUp(float seconds)
    {
        StoppedSeconds = seconds;
        IsTimedOut = true;
        StopEffect();
        SetEmission(0f, false);
        ChangeState(DisplayState.TimedOut);
        OnTimedOut?.Invoke(this, seconds);
    }

    /// <summary>ラウンドが終わった。光はそのまま(止めた人は光ったまま)</summary>
    public void EndRound()
    {
        ChangeState(DisplayState.RoundOver);
        OnRoundOver?.Invoke(this, StoppedSeconds);
    }

    /// <summary>全ラウンドが終わった。光を消して、合計の誤差を見せる状態にする</summary>
    public void EndGame(float totalError)
    {
        StopEffect();
        SetEmission(0f, false);
        ChangeState(DisplayState.GameOver);
        OnGameOver?.Invoke(this, totalError);
    }


    // ==================== ここから下は内部の処理 ====================

    private void Awake()
    {
        ResolveSlotIndex();
        ResolveReferences();
        SetEmission(0f, false);
        if (ring != null) { ring.gameObject.SetActive(false); }
    }

    private void OnDestroy()
    {
        // 実行中に作ったマテリアルの複製を片付ける(残すとメモリが増えていくため)
        if (ringMaterial != null) { Destroy(ringMaterial); }
        if (glowMaterials == null) { return; }
        foreach (Material m in glowMaterials)
        {
            if (m != null) { Destroy(m); }
        }
    }

    // 状態を変えて、合図を出す
    private void ChangeState(DisplayState next)
    {
        State = next;
        OnStateChanged?.Invoke(this, next);
    }

    // 枠番号が -1 なら、親の机の名前(Desk_1P → 0, Desk_2P → 1 …)から決める
    private void ResolveSlotIndex()
    {
        if (slotIndex >= 0) { return; }

        for (Transform t = transform; t != null; t = t.parent)
        {
            string n = t.name;
            int p = n.LastIndexOf('P');
            int underscore = n.LastIndexOf('_');
            if (p > underscore && underscore >= 0 && int.TryParse(n.Substring(underscore + 1, p - underscore - 1), out int number))
            {
                slotIndex = number - 1;
                return;
            }
        }
        Debug.LogWarning($"[StopwatchDisplay] {name} の枠番号が決まりません。Inspector の Slot Index を設定してください");
    }

    // 文字・リング・光らせる部品・光の色を、設定されていなければ子から探す
    private void ResolveReferences()
    {
        if (timeText == null)
        {
            Transform t = transform.Find("Watch/TimeText");
            if (t != null) { timeText = t.GetComponent<TMP_Text>(); }
        }

        if (ring == null) { ring = transform.Find("Watch/Ring"); }

        if (glowRenderers == null || glowRenderers.Length == 0)
        {
            List<Renderer> list = new List<Renderer>();
            Transform bezel = transform.Find("Watch/Bezel");
            if (bezel != null && bezel.GetComponent<Renderer>() != null) { list.Add(bezel.GetComponent<Renderer>()); }
            glowRenderers = list.ToArray();
        }

        // 光の色が未設定なら、本体の色(= 机ごとに設定したプレイヤーの色)を使う
        if (glowColor.a <= 0f)
        {
            Transform body = transform.Find("Watch/Body");
            Renderer bodyRenderer = body != null ? body.GetComponent<Renderer>() : null;
            glowColor = bodyRenderer != null && bodyRenderer.sharedMaterial != null && bodyRenderer.sharedMaterial.HasProperty("_BaseColor")
                ? bodyRenderer.sharedMaterial.GetColor("_BaseColor")
                : Color.white;
        }

        // 光らせる部品のマテリアルを複製する(renderer.material で、その部品専用の複製になる)
        glowMaterials = new Material[glowRenderers.Length];
        originalColors = new Color[glowRenderers.Length];
        for (int i = 0; i < glowRenderers.Length; i++)
        {
            if (glowRenderers[i] == null) { continue; }
            glowMaterials[i] = glowRenderers[i].material;
            glowMaterials[i].EnableKeyword("_EMISSION"); // URP の Lit で発光を使うためのスイッチ
            originalColors[i] = glowMaterials[i].HasProperty("_BaseColor") ? glowMaterials[i].GetColor("_BaseColor") : Color.white;
        }

        // リングのマテリアルも複製して、プレイヤーの色にしておく(半透明の Unlit なので、部屋の明るさに関係なく同じ色で見える)
        if (ring != null)
        {
            Renderer r = ring.GetComponent<Renderer>();
            if (r != null) { ringMaterial = r.material; }
        }
    }

    // 縁の発光の強さを変える(0 = 消す)。lit が true なら、縁の色もプレイヤーの色にする
    // ライトは使わない = 部屋のライトの数の上限に影響しない
    private void SetEmission(float intensity, bool lit)
    {
        if (glowMaterials == null) { return; }

        Color emission = glowColor * intensity;
        for (int i = 0; i < glowMaterials.Length; i++)
        {
            Material m = glowMaterials[i];
            if (m == null) { continue; }
            m.SetColor("_EmissionColor", emission);
            m.SetColor("_BaseColor", lit ? glowColor : originalColors[i]);
        }
    }

    // リングの大きさと濃さを変える
    private void SetRing(float scale, float alpha)
    {
        if (ring == null) { return; }
        ring.localScale = Vector3.one * scale;
        if (ringMaterial != null)
        {
            // プレイヤーの色を少し白に寄せて、明るく見えるようにする
            Color c = Color.Lerp(glowColor, Color.white, ringBrightness);
            c.a = alpha;
            ringMaterial.SetColor("_BaseColor", c);
        }
    }

    // 途中のエフェクトを止めて、リングを隠す
    private void StopEffect()
    {
        if (effectRoutine != null)
        {
            StopCoroutine(effectRoutine);
            effectRoutine = null;
        }
        if (ring != null) { ring.gameObject.SetActive(false); }
    }

    // 押した瞬間のエフェクト
    // リング: 外周の大きさ → ringEndScale まで広がりながら、だんだん透明になって消える(ringDuration 秒)
    // 縁   : 最初は強く光り、リングが消えるころには glowIntensity まで落ち着く(その後も光ったまま)
    private IEnumerator StopEffectRoutine()
    {
        if (ring != null) { ring.gameObject.SetActive(true); }

        float t = 0f;
        while (t < ringDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / ringDuration);       // 0 → 1
            float easeOut = 1f - (1f - k) * (1f - k) * (1f - k); // 最初に速く広がり、最後はゆっくり

            // 濃さは、前半はほとんど薄くならず、後半で一気に消える(k の2乗で減らす)ので、広がる様子が見えやすい
            SetRing(Mathf.Lerp(ringStartScale, ringEndScale, easeOut), ringStartAlpha * (1f - k * k));
            SetEmission(Mathf.Lerp(flashIntensity, glowIntensity, easeOut), true);
            yield return null;
        }

        if (ring != null) { ring.gameObject.SetActive(false); }
        SetEmission(glowIntensity, true);
        effectRoutine = null;
    }
}
