using TMPro;
using UnityEngine;

/// <summary>
/// テレビに今のラウンドの目標時間を表示する。テレビの画面の文字(TextMeshPro)に付ける。
///
/// ネットワークについて
/// ・目標時間は、サーバーの StopwatchMinigame が「目標決定」の出来事で全員に送っている
/// ・ここでは MinigameInfo の出来事を聞くだけでよい(NetworkObject は不要)
/// </summary>
public class StopwatchTargetView : MonoBehaviour
{
    [Tooltip("目標時間を出す文字。空なら同じオブジェクトの文字を使う")]
    [SerializeField] private TMP_Text targetText;

    private void Awake()
    {
        if (targetText == null) { targetText = GetComponent<TMP_Text>(); }

        // 最初のラウンドの目標が届くまでは何も出さない
        targetText.text = "";
    }

    // イベントは OnEnable で登録して、OnDisable で外す(MinigameInfo の決まり)
    private void OnEnable()
    {
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
        MinigameInfo.OnCustomEvent += HandleCustomEvent;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
        MinigameInfo.OnCustomEvent -= HandleCustomEvent;
    }

    // 次のラウンドの準備(画面が暗い間)になったら、前のラウンドの目標を消す
    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        if (phase != MinigamePhase.RoundReset) { return; }

        targetText.text = "";
    }

    // 「目標決定」の出来事が届いたら、テレビの文字を書き換える
    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
    {
        if (eventName != StopwatchMinigame.TargetEventName) { return; }

        targetText.text = StopwatchTimeFormat.Format(value.FloatValue); // 例: 10秒 → "10:00"
    }
}
