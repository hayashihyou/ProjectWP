using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using System;
using Cysharp.Threading.Tasks;


/// <summary>
/// ミニゲームの基盤。流れを進めて、状態を全員に同期する。各ミニゲームはこれを継承して作る
/// NetworkBehaviour : ネットワークの機能(NetworkVariable,RPC)が使える
/// </summary>
public abstract class MinigameBase : NetworkBehaviour
{
    [Header("流れ")]
    [Tooltip("カウントダウンの秒数。3なら「3,2,1,スタート！」。0の場合、スタートの合図のみ")]
    [SerializeField] private int countdownSeconds = 3;

    [Header("ラウンド")]
    [Tooltip("1ラウンドの制限時間(秒)。0 = 制限なし")]
    [SerializeField] private float roundTimeLimit = 30.0f;


    // 今のフェーズ。サーバーだけが書き換え、全員が値を読める
    private readonly NetworkVariable<MinigamePhase> phase = new NetworkVariable<MinigamePhase>(MinigamePhase.WaitingForPlayers);

    // ラウンドが終わるサーバーの時刻。-1 = 制限なし(またはプレイ中ではない)
    // サーバーの時間のため、floatではなくdoubleにする
    private readonly NetworkVariable<double> roundEndServerTime = new NetworkVariable<double>(-1);

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
        ServerRunCountdownAsync().Forget();
    }


    // カウントダウンをして、プレイを始める(サーバーで呼ばれる)
    // async / await : ここで待つをゲームを止めずに書ける仕組み。awaitの行で1秒待つ間も、ゲームは動く
    // UniTaskVoid : 待つ処理を含むけど、何も返さないメソッドの戻り値の型
    // destroyCancellationToken : 待っている途中でオブジェクトが消えたら(シーン移動など)、自動で待つのをやめる
    private async UniTaskVoid ServerRunCountdownAsync()
    {
        ServerSetPhase(MinigamePhase.Countdown);
        for(int i = countdownSeconds; i >= 1; i--)
        {
            // 今の時間を呼ぶ。
            CountdownRpc(i);
            // 1秒待つ
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: destroyCancellationToken);
        }

        // スタートの合図
        CountdownRpc(0);
        // ラウンド制限時間を計算
        if(roundTimeLimit > 0)
        {
            roundEndServerTime.Value = NetworkManager.ServerTime.Time + roundTimeLimit;
        }
        else
        {
            roundEndServerTime.Value = -1;
        }
        // フェーズをプレイ中に
        ServerSetPhase(MinigamePhase.Playing);
    }


    // カウントダウンの数字を全員に知らせる(全員の端末で実行)。 0 = スタート！
    [Rpc(SendTo.Everyone)]
    private void CountdownRpc(int secondsLeft)
    {
        Debug.Log($"[MinigameBase]カウントダウン : {secondsLeft}");
    }


    /// <summary>
    /// 今のフェーズ
    /// =>とすることで読むだけのプロパティになる
    /// </summary>
    public MinigamePhase Phase => phase.Value;

    /// <summary>
    /// 残り時間(秒)。制限なしの時は-1
    /// </summary>
    public float RemainingTime
    {
        get
        {
            if (!IsSpawned) { return -1; }
            if (roundEndServerTime.Value < 0.0f) { return -1; }
            double remaining = roundEndServerTime.Value - NetworkManager.ServerTime.Time;
            return Mathf.Max(0f, (float)remaining);
        }
    }

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


    /// <summary>
    /// 今すぐラウンドを終える。サーバーでだけ呼べる(担当者が撮影した瞬間などに呼ぶ)
    /// </summary>
    protected void ServerEndRound()
    {
        // サーバーでないなら
        if(!IsServer)
        {
            Debug.LogWarning("ServerEndRoundはサーバーでしか呼べません");
            return;
        }

        // 時間切れと、担当者のServerEndRound()が同じフレームに重なっても、2回終わらないようにするため
        if (phase.Value != MinigamePhase.Playing) { return; }

        // ラウンド終了
        Debug.Log("ラウンド終了");
        ServerSetPhase(MinigamePhase.RoundEnd);
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


    private void Update()
    {
        if (!IsServer) { return; }
        if (phase.Value != MinigamePhase.Playing) { return; }
        if (roundEndServerTime.Value < 0) { return; }

        // 時刻を比べる
        if (NetworkManager.ServerTime.Time >= roundEndServerTime.Value) { ServerEndRound(); }

    }
}