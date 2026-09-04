using Unity.Netcode;
using UnityEngine;

// シーンに直接1つだけ配置して使う（Prefab化しない）
public class GameSessionState : NetworkBehaviour
{
    // どこからでもアクセスできるようにする簡易シングルトン（デバッグ用）
    public static GameSessionState Instance { get; private set; }

    // サーバーが書き換えると、繋がっている全員の画面に自動で同期される
    public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(0);

    private void Awake()
    {
        Instance = this;
    }

    // サーバー側からのみ呼び出す想定（ClientSessionHandler側から呼ぶ）
    public void IncrementPlayerCount()
    {
        if (!IsServer) return;
        PlayerCount.Value++;
    }
}