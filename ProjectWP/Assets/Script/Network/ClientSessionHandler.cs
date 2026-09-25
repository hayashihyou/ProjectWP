using UnityEngine;
using Unity.Netcode;

public class ClientSessionHandler : NetworkBehaviour
{
    // スポーンしたいキャラクタのプレバフを入れるためのもの
    [SerializeField] private GameObject characterPrefab;

    public override void OnNetworkSpawn()
    {
        // サーバー側でのみ実行する。
        // このオブジェクト自体が「接続してきたクライアント1人につき1つ」
        // Netcodeによって自動でスポーンされるので、ここでキャラクターを
        // 作ってしまえば、参加ボタンを押さなくても自動的に全員分作られる。
        if (!IsServer) return;

        SpawnCharacter();
    }

    private void SpawnCharacter()
    {
        // ゲームオブジェクトを作成(サーバー側で実行される)
        GameObject characterInstance = Instantiate(characterPrefab);
        // キャラクターのプレハブについている、NetworkObjectコンポーネントを取得する。Spawn処理が呼べるようになる
        NetworkObject networkObject = characterInstance.GetComponent<NetworkObject>();

        // OwnerClientId : このClientSessionHandlerの持ち主(=接続してきた本人)のクライアントID
        // 今作ったキャラクターの所有者もその人のPCになり、他の人の画面にもスポーンされる
        networkObject.SpawnWithOwnership(OwnerClientId);
    }
}
