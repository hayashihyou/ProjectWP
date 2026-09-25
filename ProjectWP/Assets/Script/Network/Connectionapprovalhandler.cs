using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Connectionapprovalhandler : MonoBehaviour
{
    // 最大同時接続人数
    [SerializeField] private int maxTotalPlayers = 4;

    // 参加を承認済みの人(ClientNetworkId)。切断時にここから外して、抜けた分の枠を空ける。
    // (承認して人数に数えた人だけを外すため、断った人の切断では減らない)
    private readonly HashSet<ulong> approvedClients = new HashSet<ulong>();

    private NetworkManager networkManager;

    /** アタッチしたゲームオブジェクトが有効になったとき1回だけ実行される初期化処理 */
    private void Awake()
    {
        // ゲームオブジェクトについているNetworkManagerを取得
        networkManager = GetComponent<NetworkManager>();
        // 接続承認の有効化
        networkManager.NetworkConfig.ConnectionApproval = true;
        // 承認するかどうかを決める
        networkManager.ConnectionApprovalCallback = ApprovalCheck;
        // 誰かが切断したら、その人が使っていた枠を必ず解放する
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
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
        // 上限チェック
        if (approvedClients.Count >= maxTotalPlayers)
        {
            response.Approved = false;      // 承認しない
            response.Reason = "満員です";   // 警告ログ
            return;
        }

        approvedClients.Add(request.ClientNetworkId);

        response.Approved = true;               // 許可する
        response.CreatePlayerObject = true;    // 接続した人ごとにプレイヤーオブジェクトを自動で作る
        response.Pending = false;               // 結果の確定
    }

    // クライアントが切断したとき(NetworkManagerが自動で呼ぶ)。
    private void HandleClientDisconnected(ulong clientId)
    {
        approvedClients.Remove(clientId);
    }
}
