using UnityEngine;
using Unity.Netcode;

public class ClientSessionHandler : NetworkBehaviour
{
    // スポーンしたいキャラクタのプレバフを入れるためのもの
    [SerializeField] private GameObject characterPrefab;

    // 1接続につき1回しかキャラクターを作らないようにするためのフラグ。
    // (キーボード/ゲームパッドでの参加ボタン経由の呼び出しと重複しても
    //  2体スポーンしないようにするための安全策)
    private bool hasSpawnedCharacter;

    public override void OnNetworkSpawn()
    {
        // サーバー側でのみ実行する。
        // このオブジェクト自体が「接続してきたクライアント1人につき1つ」
        // Netcodeによって自動でスポーンされるので、ここでキャラクターを
        // 作ってしまえば、参加ボタンを押さなくても自動的に全員分作られる。
        if (!IsServer) return;

        SpawnCharacterIfNeeded();
    }

    /**
     * [ServerRpc] : 属性、クライアントから呼んでも実際の中身は必ずサーバー側で実行される
     * ServerRpcParams rpcParams = default : 誰がServerRpcを呼んだかを自動で受け取れる
     *
     * 今はOnNetworkSpawnで自動的にキャラクターを作るようにしたので出番は無いが、
     * 将来「1台のPCから2本目のゲームパッドで追加参加する」ような機能を
     * 作るときのために残してある(LocalPlayerInputHandlerから呼ばれる)。
     */
    [ServerRpc]
    private void RequestSpawnCharacterServerRpc(ServerRpcParams rpcParams = default)
    {
        SpawnCharacterIfNeeded();
    }

    private void SpawnCharacterIfNeeded()
    {
        if (hasSpawnedCharacter) return;
        hasSpawnedCharacter = true;

        // ゲームオブジェクトを作成(サーバー側で実行される)
        GameObject characterInstance = Instantiate(characterPrefab);
        // キャラクターのプレハブについている、NetworkObjectコンポーネントを取得する。Spawn処理が呼べるようになる
        NetworkObject networkObject = characterInstance.GetComponent<NetworkObject>();

        // OwnerClientId : このClientSessionHandlerの持ち主(=接続してきた本人)のクライアントID
        // 今作ったキャラクターの所有者もその人のPCになり、他の人の画面にもスポーンされる
        networkObject.SpawnWithOwnership(OwnerClientId);
    }

    // ローカルのデバイス参加時に LocalPlayerInputHandler から呼ばれる
    public void RequestCharacterSpawn()
    {
        RequestSpawnCharacterServerRpc();
    }
}
