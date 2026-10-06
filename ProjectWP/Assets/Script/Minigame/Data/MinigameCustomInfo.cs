using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// 独自の情報の1件分
/// </summary>
public struct MinigameCustomInfo : INetworkSerializable, IEquatable<MinigameCustomInfo>
{
    public FixedString64Bytes Name; // 情報の名前(日本語なら20文字まで)
    public MinigameValue Value;     // 情報の値

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Name);
        serializer.SerializeValue(ref Value);
    }

    public bool Equals(MinigameCustomInfo other)
    {
        return Name == other.Name
            && Value.Equals(other.Value);
    }

}
