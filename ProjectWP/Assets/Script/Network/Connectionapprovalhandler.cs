using UnityEngine;
using Unity.Netcode;
using Unity.VisualScripting;

public class Connectionapprovalhandler : MonoBehaviour
{
    // 最大同時接続人数
    [SerializeField] private int maxTotalPlayers = 4;
    // 現在接続している人数
    private int currentTotalPlayers = 0;


    /** アタッチしたゲームオブジェクトが有効になったとき1回だけ実行される初期化処理 */
    private void Awake()
    {
        // ゲームオブジェクトについているNetworkManagerを取得
        NetworkManager networkManager = GetComponent<NetworkManager>();
        // 接続承認の有効化
        networkManager.NetworkConfig.ConnectionApproval = true;
        // 承認するかどうかを決める
        networkManager.ConnectionApprovalCallback = ApprovalCheck;
    }

    /** 
     * 参加させて良いか判定する
     * request : 参加人数などの情報
     * response : 参加か不参加かの結果
     */
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response
        )
    {
        // クライアントが送ってきたPayLoadから「そのPCの参加希望人数」を取り出す
        int requestedLocalPlayers = 1;
        if (request.Payload != null && request.Payload.Length >= 4) 
        {
            requestedLocalPlayers = System.BitConverter.ToInt32(request.Payload, 0);
        }

        // 上限チェック
        if(currentTotalPlayers + requestedLocalPlayers > maxTotalPlayers)
        {
            response.Approved = false;      // 承認しない
            response.Reason = "満員です";   // 警告ログ
            return;
        }

        // 1台のPCからゲームパッド2本で同時に2人参加するケースに対応するため、++にしない
        currentTotalPlayers += requestedLocalPlayers;

        response.Approved = true;               // 許可する
        response.CreatePlayerObject = true;    // 自動でプレイヤーを作らないで
        response.Pending = false;               // 結果の確定
    }


    // クライアントが切断したときに呼ぶ想定
    public void OnClientLeft(int localPlayerCountOfThatClient)
    {
        currentTotalPlayers -= localPlayerCountOfThatClient;
    }
}
