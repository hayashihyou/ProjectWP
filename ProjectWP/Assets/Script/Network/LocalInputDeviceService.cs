using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 「この端末を操作している人が、キーボード派かゲームパッド派か」を覚えておくクラス。
///
/// タイトル画面で最初に押されたボタン(キーボード/マウス/ゲームパッドのいずれか)を見て、
/// それ以降その端末はそのデバイスで操作しているものとして扱う。
/// シーンをまたいで使い続けるので、DontDestroyOnLoadで生かしておく。
///
/// 今はUIの操作方式(このデバイスの入力だけ受け付ける、など)には使っていないが、
/// 後で「実際のゲーム操作もこのデバイスに合わせる」といった用途にそのまま使える。
/// </summary>
public class LocalInputDeviceService : MonoBehaviour
{
    public static LocalInputDeviceService Instance { get; private set; }

    // PlayerControls.inputactionsのコントロールスキーム名と合わせてある("GamePad" / "Keyboard")
    public const string GamePadScheme = "GamePad";
    public const string KeyboardScheme = "Keyboard";

    // まだ何も入力されていない場合はnull
    public InputDevice ChosenDevice { get; private set; }
    public string ChosenScheme { get; private set; }

    /// <summary>
    /// 「今、UIをマウスで操作しているか、キーボード/ゲームパッドで操作しているか」。
    /// 上のChosenScheme(そのプレイヤーがゲーム操作に使うデバイス)とは別の概念で、
    /// こちらは「直近どちらの入力方式が使われたか」を毎フレーム追いかけ続けるもの。
    /// UI側の「選択中を光らせる/ホバー中を光らせる」の切り替えに使う。
    /// </summary>
    public enum UiInputMode
    {
        KeyboardOrGamePad,
        Mouse,
    }

    // 初期値はKeyboardOrGamePad。まだ何も操作していない段階では、最初にコードで
    // 選択しておいたUI要素(サーバー選択のServer1行など)が光って見えるようにするため。
    public UiInputMode CurrentUiMode { get; private set; } = UiInputMode.KeyboardOrGamePad;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        // マウス/キーボード/ゲームパッドを問わず、あらゆる入力デバイスのイベントを監視する。
        // どのデバイスからのイベントかを見て、今どちらの操作方式が使われているかを更新する。
        InputSystem.onEvent += OnAnyInputEvent;
    }

    private void OnDisable()
    {
        InputSystem.onEvent -= OnAnyInputEvent;
    }

    private void OnAnyInputEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (device is Mouse)
        {
            CurrentUiMode = UiInputMode.Mouse;
        }
        else if (device is Keyboard || device is Gamepad)
        {
            CurrentUiMode = UiInputMode.KeyboardOrGamePad;
        }
        // それ以外のデバイス種別(未対応のもの)は無視する。
    }

    /// <summary>
    /// 入力元のデバイスを記録する。マウスもキーボードと同じ扱いにする
    /// (「マウスのクリックならキーボードで操作」という仕様のため)。
    /// </summary>
    public void SetChosenDevice(InputDevice device)
    {
        ChosenDevice = device;
        ChosenScheme = device is Gamepad ? GamePadScheme : KeyboardScheme;

        Debug.Log($"[LocalInputDeviceService] Chosen device: {device.displayName} ({ChosenScheme})");
    }
}
