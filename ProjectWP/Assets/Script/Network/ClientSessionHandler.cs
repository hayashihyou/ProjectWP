using UnityEngine;
using Unity.Netcode;

public class ClientSessionHandler : NetworkBehaviour
{
    // スポーンしたいキャラクタのプレバフを入れるためのもの
    [SerializeField] private GameObject characterPrefab;

    //public override void OnNetworkSpawn()
    //{
    //    // ネットワークオブジェクトの所有者が自分かどうかを判定
    //    // 自分の画面に同期表示させるため、同じ処理を2度3度呼ばれるのを防ぐ
    //    if (!IsOwner) return;

    //    // 自分の所有物だと確認出来たら、サーバーにキャラクターを作ってもらう用頼む
    //    RequestSpawnCharacterServerRpc();
    //}

    /** 
     * [ServerRpc] : 属性、クライアントから呼んでも実際の中身は必ずサーバー側で実行される
     * ServerRpcParams rpcParams = default : 誰がServerRpcを呼んだかを自動で受け取れる
     */
    [ServerRpc]
    private void RequestSpawnCharacterServerRpc(ServerRpcParams rpcParams = default)
    {
        // ゲームオブジェクトを作成(サーバー側で実行される)
        GameObject characterInstance = Instantiate(characterPrefab);
        // キャラクターのプレハブについている、NetworkObjectコンポーネントを取得する。Spawn処理が呼べるようになる
        NetworkObject networkObject = characterInstance.GetComponent<NetworkObject>();
        
        // rpcParams.Receive.SenderClientId : このServerRpcを呼んだクライアントのID
        // 今作ったキャラクターの所有者が、頼んできた本人のＰＣになり、他の人の画面にもスポーンされる
        networkObject.SpawnWithOwnership(rpcParams.Receive.SenderClientId);

        // todo for test : テストとしてオンラインでできるか確認
        // GameSessionStateは01_Titleにしか置かれていないデバッグ用オブジェクトなので、
        // シーン遷移後(02_PlayerJoinなど)には存在しない。存在するときだけカウントする。
        if (GameSessionState.Instance != null)
        {
            GameSessionState.Instance.IncrementPlayerCount();
        }
    }

    // ローカルのデバイス参加時に LocalPlayerInputHandler から呼ばれる
    public void RequestCharacterSpawn()
    {
        RequestSpawnCharacterServerRpc();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
