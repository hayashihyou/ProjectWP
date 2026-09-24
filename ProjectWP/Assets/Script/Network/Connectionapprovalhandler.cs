using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Connectionapprovalhandler : MonoBehaviour
{
    // 最大同時接続人数
    [SerializeField] private int maxTotalPlayers = 4;
    // 現在接続している人数
    private int currentTotalPlayers = 0;

    // 誰(ClientNetworkId)が何人分の枠を使っているかを覚えておく。
    // 切断時にこれを見て、その分だけcurrentTotalPlayersを正しく減らすため
    // (減らさないと、抜けた分の枠がいつまでも埋まったまま扱われてしまう)。
    private readonly Dictionary<ulong, int> localPlayerCountByClient = new Dictionary<ulong, int>();

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
        // このクライアントが何人分の枠を使ったかを覚えておく(切断時に減らす分)
        localPlayerCountByClient[request.ClientNetworkId] = requestedLocalPlayers;

        response.Approved = true;               // 許可する
        response.CreatePlayerObject = true;    // 自動でプレイヤーを作らないで
        response.Pending = false;               // 結果の確定
    }

    // クライアントが切断したとき(NetworkManagerが自動で呼ぶ)。
    // 承認時に記録しておいた人数分だけ、使用中人数を正しく戻す。
    private void HandleClientDisconnected(ulong clientId)
    {
        if (localPlayerCountByClient.TryGetValue(clientId, out int localPlayerCount))
        {
            currentTotalPlayers -= localPlayerCount;
            localPlayerCountByClient.Remove(clientId);
        }
    }
}
