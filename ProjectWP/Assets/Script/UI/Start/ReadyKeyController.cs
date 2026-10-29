using System.Collections;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 指定したキーが押されたら、テキスト切り替え・拡縮/フラッシュ・フェードを再生する。
/// ReadyMode で「4人が別々のボタン(デモ)」と「4人が同じボタン(本実装)」を切り替えられる。
/// </summary>
public class ReadyKeyController : MonoBehaviour
{
    public enum ReadyMode
    {
        EachPlayerOwnButton,   // 4人がそれぞれ別のボタン(デモ用。各ReadyKeyControllerが1つずつ通知)
        SameButtonPressCount   // 4人が同じボタンを押す(本実装用。押した回数を数える)
    }

    [Header("モード切り替え")]
    [SerializeField] private ReadyMode readyMode = ReadyMode.EachPlayerOwnButton;

    [Tooltip("SameButtonPressCount 時、この回数押されたら完了とする")]
    [SerializeField, Min(1)] private int requiredPressCount = 4;

    [Tooltip("SameButtonPressCount 時、押すたびに拡縮・フラッシュを再生するか")]
    [SerializeField] private bool playFeedbackOnEachPress = true;

    [Header("再生するスクリプト")]
    [SerializeField] private ReadyTextSwitcher textSwitcher;
    [SerializeField] private ButtonPunchScale punchScale;
    [SerializeField] private ButtonWhiteFlash whiteFlash;
    [SerializeField] private ScreenFader fader;

    [Header("スタート表示(フェード完了後)")]
    [Tooltip("このコントローラーがスタート表示を担当するか。別々のボタンモードでは4つのうち1つだけオンにする")]
    [SerializeField] private bool isStartOwner = false;

    [Tooltip("フェード完了後に表示するスタートのオブジェクト(任意)。最初は非アクティブにしておく")]
    [SerializeField] private GameObject startObject;

    [Tooltip("フェード完了後に呼ぶ処理。スタートのアニメーションスクリプトの Play() などを登録する")]
    [SerializeField] private UnityEvent onStartDisplay;

    [Tooltip("フェード完了から何秒後にスタートを表示するか")]
    [SerializeField, Min(0f)] private float startDelay = 0f;

    [Header("再生トリガー")]
    [Tooltip("キー入力で再生するか")]
    [SerializeField] private bool playOnKey = true;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    [Tooltip("再生に使うキー(新Input System)")]
    [SerializeField] private Key triggerKey = Key.Space;
#else
    [Tooltip("再生に使うキー(旧Input Manager)")]
    [SerializeField] private KeyCode triggerKey = KeyCode.Space;
#endif

    [Tooltip("シーン開始時に自動で再生するか")]
    [SerializeField] private bool playOnStart = false;

    [Tooltip("一度しか反応しないようにするか")]
    [SerializeField] private bool oneShot = true;

    private bool triggered;
    private int pressCount;
    private bool startPlayed;
    private Coroutine startRoutine;

    public int PressCount => pressCount;

    private void OnEnable()
    {
        if (fader != null) fader.OnFadeCompleted += HandleFadeCompleted;
    }

    private void OnDisable()
    {
        if (fader != null) fader.OnFadeCompleted -= HandleFadeCompleted;
    }

    private void Start()
    {
        if (playOnStart) Trigger();
    }

    private void Update()
    {
        if (!playOnKey) return;
        if (!IsKeyPressedThisFrame()) return;

        if (readyMode == ReadyMode.SameButtonPressCount) PressSameButton();
        else Trigger();
    }

    private bool IsKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        if (triggerKey == Key.None) return false;
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[triggerKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(triggerKey);
#endif
    }

    /// <summary>【別々のボタン】演出をまとめて再生(他のスクリプトから呼んでもOK)</summary>
    public void Trigger()
    {
        if (oneShot && triggered) return;
        triggered = true;

        if (textSwitcher != null) textSwitcher.Play();
        if (punchScale != null) punchScale.Play();
        if (whiteFlash != null) whiteFlash.Play();

        // 「OKになった」と通知。全員揃うまでフェードしない
        if (fader != null) fader.NotifyReady(this);
    }

    /// <summary>【同じボタン】押された回数を数え、規定回数でまとめて演出を再生する</summary>
    public void PressSameButton()
    {
        if (oneShot && triggered) return;

        pressCount++;

        // 押すたびのフィードバック(不要ならインスペクターでオフ)
        if (playFeedbackOnEachPress)
        {
            if (punchScale != null) punchScale.Play();
            if (whiteFlash != null) whiteFlash.Play();
        }

        // 規定回数に達したら、テキスト切り替えとフェードを再生
        if (pressCount >= requiredPressCount)
        {
            triggered = true;
            if (textSwitcher != null) textSwitcher.Play();
            if (fader != null) fader.Play();
        }
    }

    /// <summary>フェード完了時に呼ばれる。担当のコントローラーだけがスタートを表示する</summary>
    private void HandleFadeCompleted()
    {
        if (!isStartOwner || startPlayed) return;
        startPlayed = true;
        startRoutine = StartCoroutine(StartRoutine());
    }

    private IEnumerator StartRoutine()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        if (startObject != null) startObject.SetActive(true);
        onStartDisplay?.Invoke();
        startRoutine = null;
    }

    /// <summary>もう一度押せる状態に戻したい時に呼ぶ(ReadyTextSwitcher.ResetState と併用)</summary>
    public void ResetTrigger()
    {
        triggered = false;
        pressCount = 0;
        startPlayed = false;
        if (startRoutine != null) { StopCoroutine(startRoutine); startRoutine = null; }
        if (startObject != null) startObject.SetActive(false);
        if (fader != null) fader.NotifyUnready(this);
    }
}