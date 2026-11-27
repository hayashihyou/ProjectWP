using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Connectionapprovalhandler : MonoBehaviour
{
    // 断ったときの理由。参加者の画面では ServerListUI がこの文を見て、表示する文を決める
    public const string RoomFullReason = "{満員|まんいん}です";
    public const string RoomPlayingReason = "ゲーム{中|ちゅう}です";

    // 参加を承認済みの人(ClientNetworkId)と、その人のLobbyでのプレイヤーID。
    // 切断時にここから外して、抜けた分の枠を空ける。
    // (承認して人数に数えた人だけを外すため、断った人の切断では減らない)
    // Lobbyのプレイヤーは、切断されたときにホストがLobbyから外すのに使う。
    private readonly Dictionary<ulong, string> approvedClients = new Dictionary<ulong, string>();

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
        NetworkBootstrap bootstrap = NetworkBootstrap.Instance;

        // 遊び始めた後は、新しい人を入れない
        // (サーバー一覧の「プレイ中」をすり抜けて、ほぼ同時に来た人がここで断られる)
        if (bootstrap != null && bootstrap.IsRoomPlaying)
        {
            response.Approved = false;
            response.Reason = RoomPlayingReason;
            return;
        }

        // 上限チェック。人数の上限は NetworkBootstrap(Relay/Lobbyと同じ値)の1か所だけで決める
        int maxPlayers = bootstrap != null ? bootstrap.MaxPlayersPerRoom : 4;
        if (approvedClients.Count >= maxPlayers)
        {
            response.Approved = false;      // 承認しない
            response.Reason = RoomFullReason;
            return;
        }

        // 参加者が接続するときに送ってきた、Lobbyでのプレイヤー ID(ホスト自身は送ってこないので空)
        string lobbyPlayerId = request.Payload != null && request.Payload.Length > 0
            ? System.Text.Encoding.UTF8.GetString(request.Payload)
            : null;
        approvedClients[request.ClientNetworkId] = lobbyPlayerId;

        response.Approved = true;               // 許可する
        response.CreatePlayerObject = true;    // 接続した人ごとにプレイヤーオブジェクトを自動で作る
        response.Pending = false;               // 結果の確定
    }

    // クライアントが切断したとき(NetworkManagerが自動で呼ぶ)。
    private void HandleClientDisconnected(ulong clientId)
    {
        if (!approvedClients.TryGetValue(clientId, out string lobbyPlayerId)) return;
        approvedClients.Remove(clientId);

        // ホストなら、切断された人をLobbyからも外す。
        // (アプリが落ちた・回線が切れた人は自分でLobbyから抜けられず、席が残ってしまうため。
        //  普通に「タイトルに戻る」で抜けた人は、自分で先に抜けているので何も起きない)
        if (networkManager.IsServer && clientId != NetworkManager.ServerClientId && NetworkBootstrap.Instance != null)
        {
            NetworkBootstrap.Instance.RemovePlayerFromHostedLobby(lobbyPlayerId);
        }
    }
}
