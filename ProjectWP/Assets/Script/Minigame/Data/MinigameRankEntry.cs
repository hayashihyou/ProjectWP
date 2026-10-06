using Unity.Netcode;

/// <summary>
/// 結果の一人分。誰が何位で、最終的な値はいくつか。
/// </summary>
public struct MinigameRankEntry : INetworkSerializable
{
    public ulong ClientId;  // どの端末か
    public int SlotIndex;   // 枠番号(0 = 1P)
    public int Rank;        // 順位(同着は同じ順位)
    public float Value;     // 最終的な値(まとめ方の設定で計算したもの)


    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref SlotIndex);
        serializer.SerializeValue(ref Rank);
        serializer.SerializeValue(ref Value);
    }
}