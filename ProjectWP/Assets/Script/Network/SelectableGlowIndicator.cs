using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ボタンなどの縁取り(Outline)を、操作方式に応じて光らせるかどうか切り替える。
///
/// - キーボード/ゲームパッドで操作しているとき: EventSystemの「選択中」になっている
///   ものだけを光らせる(上下移動でフォーカスが当たっているかどうかで判定)。
/// - マウスで操作しているとき: マウスポインタが実際に乗っている(ホバー中の)
///   ものだけを光らせる。クリックして選択状態になっていても、ポインタが
///   離れれば光らなくする。
///
/// 「今どちらの操作方式が使われているか」はLocalInputDeviceService.CurrentUiModeを見る
/// (あらゆる入力デバイスのイベントを監視して毎フレーム更新されている)。
/// 操作方式は途中で切り替わりうる(マウスを触った直後にゲームパッドを触す、など)ため、
/// 選択/ホバーの状態が変わった瞬間だけでなく、Updateで毎フレーム再評価している。
///
/// 使い方: 対象のGameObjectにOutlineコンポーネントを付けた上でこのスクリプトも付け、
/// outlineに参照をセットする。
/// </summary>
[RequireComponent(typeof(Selectable))]
public class SelectableGlowIndicator : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Outline outline;

    // キーボード/ゲームパッドでの「選択中」と、マウスの「ホバー中」を別々に覚えておく。
    // どちらを実際の見た目(Outlineの点灯)に反映するかは、今の操作方式で切り替える。
    private bool isSelected;
    private bool isHovering;

    private void Awake()
    {
        if (outline == null) outline = GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
    }

    private void Update()
    {
        UpdateGlow();
    }

    public void OnSelect(BaseEventData eventData)
    {
        isSelected = true;
        UpdateGlow();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        isSelected = false;
        UpdateGlow();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        UpdateGlow();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        UpdateGlow();
    }

    private void UpdateGlow()
    {
        if (outline == null) return;

        bool usingMouse = LocalInputDeviceService.Instance != null
            && LocalInputDeviceService.Instance.CurrentUiMode == LocalInputDeviceService.UiInputMode.Mouse;

        outline.enabled = usingMouse ? isHovering : isSelected;
    }
}
