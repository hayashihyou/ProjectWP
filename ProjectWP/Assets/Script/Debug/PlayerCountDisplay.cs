using TMPro;
using UnityEngine;

// Canvas配下のGameObjectにアタッチして使う（デバッグ表示用）
public class PlayerCountDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerCountText;

    private void OnEnable()
    {
        // GameSessionState.Instanceは、GameSessionState自身のAwake()が
        // まだ実行されていないタイミングだとnullのことがある(実行順序の問題)。
        // その場合はシーンから直接探し、見つけた参照をそのまま使う
        // (Instanceを読み直すと再びnullの可能性があるため)。
        GameSessionState session = GameSessionState.Instance;
        if (session == null)
        {
            session = FindFirstObjectByType<GameSessionState>();
            if (session == null) return;
        }

        session.PlayerCount.OnValueChanged += UpdateText;
        UpdateText(0, session.PlayerCount.Value);
    }

    private void OnDisable()
    {
        if (GameSessionState.Instance != null)
        {
            GameSessionState.Instance.PlayerCount.OnValueChanged -= UpdateText;
        }
    }

    private void UpdateText(int previousValue, int newValue)
    {
        playerCountText.text = $"players: {newValue} / 4";
    }
}