using UnityEngine;
using Unity.Netcode;

/// <summary>
/// シーンが始まったときに、全員のキャラクターを「そのシーンの初期位置」に並べる。
///
/// キャラクターはシーンをまたいで生き続けるので、何もしないと待機画面で立っていた位置
/// (1P〜4Pの枠の位置)のまま次のシーンが始まってしまう。そこで、ゲーム用のシーンに
/// これを置き、待機画面で決まった枠の番号(1P〜4P)と同じ番号の初期位置に移動させる。
///
/// 使い方: シーンに空のGameObjectを作ってこれを付け、spawnPointsに
/// 1P〜4Pの順で初期位置のTransformを設定する。
/// 動かすのはサーバーだけ。各キャラクターは所有者のPCで位置を決めているので、
/// PlayerSlot.MoveTo経由で所有者に「ここに立って」と頼む。
/// </summary>
public class PlayerSpawnPoints : MonoBehaviour
{
    [Tooltip("1P〜4Pの順に、そのシーンでの初期位置。向きもこのTransformの向きに合わせる")]
    [SerializeField] private Transform[] spawnPoints = new Transform[PlayerSlotManager.SlotCount];

    private void Start()
    {
        // ネットワークに繋がっていない(シーン単体で試している)場合や、参加者側のPCでは何もしない
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer) return;

        foreach (PlayerSlot player in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            int slot = player.SlotIndex;
            if (slot < 0 || slot >= spawnPoints.Length || spawnPoints[slot] == null) continue;

            player.MoveTo(spawnPoints[slot].position, spawnPoints[slot].rotation);
        }
    }
}
