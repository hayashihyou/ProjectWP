using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class DebugPlayerJoinText : MonoBehaviour
{
    // インスペクター上で割り当てられるようにする
    [Tooltip("参加者リストを表示する")]
    [SerializeField] private TextMeshProUGUI joinText; // 参加者の名前用テキスト

    // 1P参加、2P参加と参加するたび文字列を追記していく入れ物
    private readonly StringBuilder joinLog = new StringBuilder();

    // プレイヤーの入力
    private PlayerInputManager playerInputManager;


    /** スクリプトが有効になったタイミングで処理が自動的に呼ばれる */
    private void OnEnable()
    {
        // シーン内のPlayerInputManagerを探して、参照を保持
        // Managerはそのシーンに1つしかない想定
        playerInputManager = FindFirstObjectByType<PlayerInputManager>();

        // 参加イベントが発生したら、メソッドを呼ぶ
        if (playerInputManager != null) { playerInputManager.onPlayerJoined += OnPlayerJoined; }
        // シーンに存在しないなら警告
        else { Debug.LogWarning("PlayerInputManagerがシーン内に見つかりません。"); }
    }

    /** プレイヤーが一人参加するたび、PlayerInputManagerから呼び出される処理 */
    private void OnPlayerJoined(PlayerInput playerInput)
    {
        // playerIndexは0から始まる番号なので、+1して1Pの表記に合わせる
        int playerNumber = playerInput.playerIndex + 1;
        // 参加ログに、新しい行として追加
        joinLog.AppendLine($"｛playerNumber｝P参加");

        // テキストが割り当てられていれば、画面表示を更新
        if (joinText != null) { joinText.text = joinLog.ToString(); }
    }

    // このスクリプトが無効になったタイミングでUnityから自動的に呼ばれる
    private void OnDisable()
    {
        // OnEnableで登録した処理を解除
        if (playerInputManager != null) { playerInputManager.onPlayerJoined -= OnPlayerJoined; }
    }
}
