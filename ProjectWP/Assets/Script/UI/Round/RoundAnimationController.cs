using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ラウンドの文字アニメーション(RoundTextAnimation)と、
/// 点のアニメーション(RoundDotAnimation)を一緒に再生するスクリプト。
///
/// やること:
///   - 点の元オブジェクト(roundDot)を Total Rounds の数だけ複製して並べる
///   - 現在のラウンドの点を大きく、それ以外を小さくする
///   - Play() で文字と点の脈動を同時に再生する
///   - スペースキーを押すたびに再生し、2回目以降はラウンドを1つ進める
/// </summary>
public class RoundAnimationController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("文字のアニメーション(RoundTextAnimation)")]
    [SerializeField] private RoundTextAnimation roundText;

    [Tooltip("点の元になるオブジェクト(RoundDotAnimation を付けた roundDot)。シーン上でもPrefabでもOK")]
    [SerializeField] private RoundDotAnimation dotTemplate;

    [Tooltip("複製した点を入れる親(Horizontal Layout Group を付けると自動で横並びになる)。元の点の親とは別のオブジェクトにすること")]
    [SerializeField] private Transform dotsParent;

    [Tooltip("ラウンドの数字を表示するTMPテキスト(「1」「2」…)。ラウンドが変わると自動で書き換わる")]
    [SerializeField] private TMP_Text roundNumberText;

    [Tooltip("テキスト全体の書式。{0} にラウンド数が入る。改行は \\n と書くか、Enterキーで入力する(例: 「{0}」→「2」、「Round\\n{0}」→「Round(改行)2」)")]
    [TextArea(1, 3)]
    [SerializeField] private string numberFormat = "{0}";

    [Header("ラウンド設定")]
    [Tooltip("全部で何ラウンドあるか(1〜10)。この数だけ点が表示される")]
    [Range(1, 10)]
    [SerializeField] private int totalRounds = 3;

    [Tooltip("現在のラウンド(1始まり)")]
    [SerializeField] private int currentRound = 1;

    [Tooltip("最終ラウンドの次に進んだとき、1ラウンド目に戻る(OFFなら最終ラウンドで止まる)")]
    [SerializeField] private bool loopAfterLastRound = true;

    [Tooltip("ラウンドが変わった時に呼ばれる(文字を「ROUND 2」に変えるときなどに使う)")]
    [SerializeField] private UnityEvent<int> onRoundChanged;

    [Header("フェード")]
    [Tooltip("点を文字と同じタイミングでフェードイン/フェードアウトさせる(点の親に CanvasGroup が自動で付く)")]
    [SerializeField] private bool fadeDotsWithText = true;

    [Header("キー操作")]
    [Tooltip("キーを押したときにアニメーションを再生する(OFFにするとキー操作なし。Play() を呼んだ時だけ再生)")]
    [FormerlySerializedAs("playOnSpaceKey")]
    [SerializeField] private bool playOnKey = true;

#if ENABLE_INPUT_SYSTEM
    [Tooltip("アニメーションを再生するキー(プルダウンで選択)")]
    [SerializeField] private Key triggerKey = Key.Space;
#else
    [Tooltip("アニメーションを再生するキー(プルダウンで選択)")]
    [SerializeField] private KeyCode triggerKey = KeyCode.Space;
#endif

    [Tooltip("キーを押すたびにラウンドを1つ進める(最初の1回目は現在のラウンドを再生)")]
    [FormerlySerializedAs("advanceRoundOnSpace")]
    [SerializeField] private bool advanceRoundOnKey = true;

    [Tooltip("再生中にキーを押した場合、最初からやり直す(OFFなら再生中は無視)")]
    [SerializeField] private bool restartWhilePlaying = true;

    private readonly List<RoundDotAnimation> dots = new List<RoundDotAnimation>();
    private bool hasPlayed;
    private Transform dotsRoot;
    private CanvasGroup dotsGroup;

    private void Awake()
    {
        BuildDots();
        ApplyDotStates();
        UpdateRoundNumberText();
        SetupDotsFade();
    }

    /// <summary>ラウンドの数字テキストを現在のラウンドに書き換える。</summary>
    private void UpdateRoundNumberText()
    {
        if (roundNumberText == null) return;
        // 「\n」と書かれていたら改行として扱う
        string format = numberFormat.Replace("\\n", "\n");
        roundNumberText.text = string.Format(format, currentRound);
    }

    /// <summary>点の親に CanvasGroup を用意し、最初は透明にしておく。</summary>
    private void SetupDotsFade()
    {
        if (!fadeDotsWithText || roundText == null || dotsRoot == null) return;

        // 点が文字の子なら、文字のフェードがそのまま引き継がれるので何もしない
        if (dotsRoot.IsChildOf(roundText.transform)) return;

        dotsGroup = dotsRoot.GetComponent<CanvasGroup>();
        if (dotsGroup == null)
        {
            dotsGroup = dotsRoot.gameObject.AddComponent<CanvasGroup>();
        }
        dotsGroup.alpha = 0f;
    }

    // 文字の更新(Update)が終わった後に、点の透明度を文字に合わせる
    private void LateUpdate()
    {
        if (dotsGroup == null || roundText == null) return;
        dotsGroup.alpha = roundText.Alpha;
    }

    private void Update()
    {
        if (!playOnKey) return;
        if (!IsTriggerKeyPressedThisFrame()) return;
        if (roundText != null && roundText.IsPlaying && !restartWhilePlaying) return;

        // 2回目以降の再生では、ラウンドを1つ進める
        if (advanceRoundOnKey && hasPlayed)
        {
            if (currentRound < totalRounds)
            {
                currentRound++;
            }
            else if (loopAfterLastRound)
            {
                currentRound = 1;
            }
        }

        Play();
    }

    private bool IsTriggerKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        // 新しい Input System
        if (triggerKey == Key.None || Keyboard.current == null) return false;
        return Keyboard.current[triggerKey].wasPressedThisFrame;
#else
        // 従来の Input Manager
        return Input.GetKeyDown(triggerKey);
#endif
    }

    /// <summary>現在のラウンドで、文字と点のアニメーションを一緒に最初から再生する。</summary>
    public void Play()
    {
        hasPlayed = true;
        ApplyDotStates();
        UpdateRoundNumberText();
        onRoundChanged?.Invoke(currentRound);

        if (roundText != null)
        {
            roundText.Play();
        }

        // 現在のラウンドの点だけ脈動する
        int index = currentRound - 1;
        if (index >= 0 && index < dots.Count && dots[index] != null)
        {
            dots[index].PlayPulse();
        }
    }

    /// <summary>ラウンドを指定して、文字と点を一緒に再生する(1始まり)。</summary>
    public void Play(int round)
    {
        SetRound(round);
        Play();
    }

    /// <summary>ラウンド番号を設定する(1始まり)。点の大小に反映される。</summary>
    public void SetRound(int round)
    {
        currentRound = Mathf.Clamp(round, 1, totalRounds);
        ApplyDotStates();
        UpdateRoundNumberText();
    }

    /// <summary>全ラウンド数を後から変更する(1〜10)。点も作り直される。</summary>
    public void SetTotalRounds(int total)
    {
        totalRounds = Mathf.Clamp(total, 1, 10);
        currentRound = Mathf.Clamp(currentRound, 1, totalRounds);
        BuildDots();
        ApplyDotStates();
    }

    /// <summary>元の点を、Total Rounds の数だけ複製して並べる。</summary>
    private void BuildDots()
    {
        if (dotTemplate == null) return;

        // 以前に作った点を片付ける
        foreach (RoundDotAnimation d in dots)
        {
            if (d != null) Destroy(d.gameObject);
        }
        dots.Clear();

        Transform parent = dotsParent != null ? dotsParent : dotTemplate.transform.parent;
        dotsRoot = parent;

        // シーン上の元オブジェクトは複製元として隠す(Prefabならそのまま)
        if (dotTemplate.gameObject.scene.IsValid())
        {
            dotTemplate.gameObject.SetActive(false);
        }

        for (int i = 0; i < totalRounds; i++)
        {
            RoundDotAnimation dot = Instantiate(dotTemplate, parent, false);
            dot.name = "Dot " + (i + 1);
            dot.gameObject.SetActive(true);
            dots.Add(dot);
        }
    }

    /// <summary>現在のラウンドの点を大きく、それ以外を小さくする。</summary>
    private void ApplyDotStates()
    {
        for (int i = 0; i < dots.Count; i++)
        {
            if (dots[i] == null) continue;
            dots[i].SetCurrent(i == currentRound - 1);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        totalRounds = Mathf.Clamp(totalRounds, 1, 10);
        currentRound = Mathf.Clamp(currentRound, 1, totalRounds);
    }
#endif
}