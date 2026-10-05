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

        // ホスト自身のキャラクターは、同期を待つ必要が無いので今すぐ作る
        if (OwnerClientId == NetworkManager.ServerClientId)
        {
            SpawnCharacter();
            return;
        }

        // 参加者のキャラクターは、その参加者の「同期」が終わってから作る。
        // この時点の参加者は、まだ「今あるオブジェクト一覧」を受け取る前の途中の状態。
        // ここでキャラクターを作ると、そのキャラクターが「一覧の中」と「新しく作った通知」の
        // 2回届いてしまい、参加者の画面で
        //   "Trying to spawn a NetworkObject but an object with that NetworkObjectId is already in the spawned list"
        //   "[Size mismatch] Expected: 51 Currently At: 0"
        // のエラーになる(枠の番号などが正しく届かない原因にもなる)。
        NetworkManager.SceneManager.OnSynchronizeComplete += OnClientSynchronizeComplete;
    }

    public override void OnNetworkDespawn()
    {
        // 同期が終わる前に抜けた場合などに、登録したままにならないようにする
        if (NetworkManager != null && NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.OnSynchronizeComplete -= OnClientSynchronizeComplete;
        }
    }

    private void OnClientSynchronizeComplete(ulong clientId)
    {
        // 他の参加者の同期完了も届くので、自分の持ち主(=このキャラクターを作る相手)の分だけ反応する
        if (clientId != OwnerClientId) return;

        NetworkManager.SceneManager.OnSynchronizeComplete -= OnClientSynchronizeComplete;
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
