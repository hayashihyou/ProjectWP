using System;
using Unity.Netcode;

/// <summary>
/// プレイヤー1人分の情報
/// </summary>
public struct MinigamePlayerState : INetworkSerializable, IEquatable<MinigamePlayerState>
{
    public ulong ClientId;      // どの端末のプレイヤーか(自分か否か)
    public int SlotIndex;     // 枠番号(0 = 1P)
    public float RoundValue;    // 今回のラウンドの値
    public float TotalValue;    // 合計の値
    public bool IsFinished;    // このラウンドが終了か
    public bool IsEliminated;  // 脱落したか
    public bool IsConnected;   // 接続中か
    public bool IsCpu;         // CPUか(将来的に、抜け番に加える可能性があるため)

    /// <summary>
    /// バイトへの変換とバイトからの復元
    /// ネットワークでどうやって送るか、受け取るか
    /// structでは送ることはできない。バイトだけ送れる
    /// 送るとき : ClientIdの値を読み取ってバイトに書き込む
    /// 受け取るとき : バイトから値を読み取って、ClientIdに入れる
    /// ref : 受け取るときに変数の中身を変える必要があるため
    /// <T>・where T : IReaderWriter : 書き込み用(Writer)と読み取り用(Reader)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="serializer"></param>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref SlotIndex);
        serializer.SerializeValue(ref RoundValue);
        serializer.SerializeValue(ref TotalValue);
        serializer.SerializeValue(ref IsFinished);
        serializer.SerializeValue(ref IsEliminated);
        serializer.SerializeValue(ref IsConnected);
        serializer.SerializeValue(ref IsCpu);
    }


    /// <summary>
    /// 同じかどうか中身が同じかどうか比べる
    /// 2つのMinigamePlayerStateを比べて、中身が全部同じならtrue
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool Equals(MinigamePlayerState other)
    {
        return ClientId == other.ClientId
            && SlotIndex == other.SlotIndex
            && RoundValue == other.RoundValue
            && TotalValue == other.TotalValue
            && IsFinished == other.IsFinished
            && IsEliminated == other.IsEliminated
            && IsConnected == other.IsConnected
            && IsCpu == other.IsCpu;
    }
};