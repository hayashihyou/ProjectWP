using System.Collections;
using Unity.Netcode;
using UnityEngine;

// PlayerInputManagerが参加時に生成する、見た目を持たない入力検知専用オブジェクト
public class LocalPlayerInputHandler : MonoBehaviour
{
    private void Start()
    {
        StartCoroutine(RequestSpawnWhenReady());
    }

    private IEnumerator RequestSpawnWhenReady()
    {
        // 接続が完了し、自分のClientSessionHandlerが用意されるまで待つ
        while (NetworkManager.Singleton == null
            || !NetworkManager.Singleton.IsConnectedClient
            || NetworkManager.Singleton.LocalClient.PlayerObject == null)
        {
            yield return null;
        }

        ClientSessionHandler session =
            NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<ClientSessionHandler>();

        if (session != null)
        {
            session.RequestCharacterSpawn();
        }
    }
}