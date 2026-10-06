using UnityEngine;


/// <summary>
/// ミニゲーム；タイマーを止める
/// </summary>
public class TimerStopMinigame : MonoBehaviour
{
    [Header("操作対象のプレイヤー")]
    [SerializeField] private PlayerMinigameInput player;

    private float elapsedTime = 0f; // 経過時間
    private float stoppedTime; //Aボタンを押した瞬間の記録
    private bool isRunning; // タイマーが動作中かどうか


    private void Start()
    {
        MinigameManager.Instance.SelectMinigame(MinigameType.TimerStop);

        // プレイヤーがAボタンを押したときの処理を購読する
        player.OnConfirmPressed -= HandleConfirmPressed;

        isRunning = true;
    }

    private void Update()
    {
         if(!isRunning) return;

         elapsedTime += Time.deltaTime;
    }
    private void HandleConfirmPressed()
    {
        if (!isRunning) return;

        isRunning = false;
        stoppedTime = elapsedTime;

        Debug.Log("止めた時間：" + stoppedTime + "秒");

        //　ここでリザルトシーンへの受け渡しを行う想定
    }
}
