using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 【開発用】サウンド確認シーンの一覧の1行(音1つ分)。
/// シーンに置いてある非表示の「Row Template」を SoundCheckController が複製して使う。
/// 見た目を変えたいときは Row Template をいじれば全部の行に反映される。
/// </summary>
public class SoundCheckRow : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private Image _background;
    [Tooltip("左端の細い線。鳴っている間だけ光る")]
    [SerializeField] private Image _accentBar;
    [Tooltip("BGM / SE の札")]
    [SerializeField] private TMP_Text _typeLabel;
    [SerializeField] private TMP_Text _label;
    [Tooltip("右端の状態表示(♪ / 待機中)")]
    [SerializeField] private TMP_Text _stateLabel;

    [Header("色")]
    [SerializeField] private Color _normalColor = new Color(1f, 1f, 1f, 0.04f);
    [SerializeField] private Color _selectedColor = new Color(1f, 1f, 1f, 0.12f);
    [SerializeField] private Color _bgmColor = new Color(0.25f, 0.88f, 0.82f);
    [SerializeField] private Color _seColor = new Color(1f, 0.3f, 0.55f);
    [SerializeField] private Color _textColor = new Color(0.86f, 0.88f, 0.95f);

    /// <summary>この行の音</summary>
    public SoundEntry Entry { get; private set; }

    private Color _categoryColor;

    /// <summary>表示する音と、押されたときの処理を設定する</summary>
    public void Setup(SoundEntry entry, Action<SoundCheckRow> onClick)
    {
        Entry = entry;

        bool bgm = entry.category == SoundCategory.Bgm;
        _categoryColor = bgm ? _bgmColor : _seColor;
        _typeLabel.text = bgm ? "BGM" : "SE";
        _typeLabel.color = _categoryColor;

        // 日本語名が未入力なら、代わりにIDを出す(入力し忘れに気づけるように)
        _label.text = string.IsNullOrEmpty(entry.usageLabel) ? entry.id : entry.usageLabel;

        _button.onClick.AddListener(() => onClick(this));
    }

    /// <summary>選んでいる・鳴っている・前の音が消えるのを待っている に合わせて見た目を変える</summary>
    public void SetState(bool selected, bool playing, bool waiting)
    {
        _background.color = selected ? _selectedColor : _normalColor;

        // 鳴っている間は左の線と文字が種類の色で光る(少し明るさが揺れる)
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
        Color bar = _categoryColor;
        bar.a = playing ? pulse : waiting ? 0.35f : 0f;
        _accentBar.color = bar;

        _label.color = playing ? _categoryColor : _textColor;
        _stateLabel.text = playing ? "♪" : waiting ? "WAIT" : "";
        _stateLabel.color = _categoryColor;
    }
}
