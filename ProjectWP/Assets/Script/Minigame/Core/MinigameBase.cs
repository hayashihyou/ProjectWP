using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ミニゲームの基盤。流れを進めて、状態を全員に同期する。各ミニゲームはこれを継承して作る
/// NetworkBehaviour : ネットワークの機能(NetworkVariable,RPC)が使える
/// </summary>
public abstract class MinigameBase : NetworkBehaviour
{

    // 今のフェーズ。サーバーだけが書き換え、全員が値を読める
    private readonly NetworkVariable<MinigamePhase> phase = new NetworkVariable<MinigamePhase>(MinigamePhase.WaitingForPlayers);

    // 「準備ができた」が届いた人のID(サーバーだけが使う)
    private readonly HashSet<ulong> readyClientIds = new HashSet<ulong>();



    // 準備ができたことをつたえる
    [Rpc(SendTo.Server)] // サーバーの端末で中身が実行
    private void NotifyReadyRpc(RpcParams rpcParams = default)　// 誰が送ってきたかの情報、RPCのメソッド名はRpcで終わる必要がある
    {
        readyClientIds.Add(rpcParams.Receive.SenderClientId);
        ServerCheckAllReady();
    }

    // 全員の準備できたがそろったか調べる(サーバーで呼ばれる)。揃ったらカウントダウンへ進む。
    private void ServerCheckAllReady()
    {
        if(Phase != MinigamePhase.WaitingForPlayers)
        {
            return;
        }

        // 接続中の全員を一人ずつ調べる
        foreach(ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!readyClientIds.Contains(clientId))
            {
                return;
            }
        }
        Debug.Log("全員揃いました");
        ServerSetPhase(MinigamePhase.Countdown);
    }


    /// <summary>
    /// 今のフェーズ
    /// =>とすることで読むだけのプロパティになる
    /// </summary>
    public MinigamePhase Phase => phase.Value;

    // フェーズを変える。サーバーだけ呼べる
    protected void ServerSetPhase(MinigamePhase next)
    {
        if (!IsServer)
        {
            Debug.LogWarning("ServerSetPhaseはサーバーでしか呼べません"); 
            return;
        }

        phase.Value = next;
    }


    // ネットワークに出てきたとき、全員の端末で呼ばれる。フェーズの変化を見張り始め、「準備できた」を送る
    public override void OnNetworkSpawn()
    {
        phase.OnValueChanged += OnPhaseChanged;
        NotifyReadyRpc();
    }
    // ネットワークから消えるとき、全員の端末で呼ばれる。見張りをやめる。
    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;
    }
    // フェーズが変わったとき、全員の端末で呼ばれる。確認用にログを出す
    private void OnPhaseChanged(MinigamePhase previous, MinigamePhase current)
    {
        Debug.Log($"[MinigameBase]フェーズ : {previous} → {current}");
    }
}