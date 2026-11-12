using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発用】Toggle を「スイッチ」の見た目にする。ON で丸が右に動いて色が付く。
/// 値は普通の Toggle(isOn)のまま使える。
/// </summary>
[RequireComponent(typeof(Toggle))]
public class SoundCheckSwitch : MonoBehaviour
{
    [SerializeField] private Image _track;
    [SerializeField] private RectTransform _knob;
    [SerializeField] private Color _onColor = new Color(0.25f, 0.88f, 0.82f);
    [SerializeField] private Color _offColor = new Color(1f, 1f, 1f, 0.15f);

    private Toggle _toggle;

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();
        _toggle.onValueChanged.AddListener(_ => Refresh());
    }

    private void OnEnable() => Refresh();

    private void Refresh()
    {
        if (_toggle == null) { _toggle = GetComponent<Toggle>(); }
        bool on = _toggle.isOn;

        _track.color = on ? _onColor : _offColor;

        // 丸を左端(OFF)か右端(ON)に寄せる
        float x = on ? 1f : 0f;
        _knob.anchorMin = new Vector2(x, 0.5f);
        _knob.anchorMax = new Vector2(x, 0.5f);
        _knob.pivot = new Vector2(x, 0.5f);
        _knob.anchoredPosition = new Vector2(on ? -3f : 3f, 0f);
    }
}
