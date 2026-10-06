using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions.Comparers;

/// <summary>
/// 独自の情報・出来事で送る値
/// int/float/bool/文字列/Vector3のどれかが入る
/// </summary>
public struct MinigameValue : INetworkSerializable,IEquatable<MinigameValue>
{
    public enum DataType : byte
    {
        None,       // なにもない
        Int,        // 整数
        Float,      // 小数
        Bool,       // true / false
        Text,       // 文字列
        Vector3     // 位置や向き
    }

    public DataType Type;                  // 今どれが入っているか
    public int IntValue;                   // Typeがintの時に使う
    public float FloatValue;               // Typeがfloatの時に使う
    public bool BoolValue;                 // Typeがboolの時に使う
    public FixedString64Bytes TextValue;   // TypeがTextの時に使う(約20文字入ります)
    public Vector3 Vector3Value;           // TypeがVector3の時に使う

    
    /// <summary>
    /// 中身が同じかどうか調べる
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Type);

        switch(Type)
        {
            case DataType.Int:
                {
                    serializer.SerializeValue(ref IntValue);
                    break;
                }
            case DataType.Float:
                {
                    serializer.SerializeValue(ref FloatValue);
                    break;
                }
            case DataType.Bool:
                {
                    serializer.SerializeValue(ref BoolValue);
                    break;
                }
            case DataType.Text:
                {
                    serializer.SerializeValue(ref TextValue);
                    break;
                }
            case DataType.Vector3:
                {
                    serializer.SerializeValue(ref Vector3Value);
                    break;
                }
        }
    }

    public bool Equals(MinigameValue other)
    {
        return Type == other.Type
            && IntValue == other.IntValue
            && FloatValue == other.FloatValue
            && BoolValue == other.BoolValue
            && TextValue == other.TextValue
            && Vector3Value == other.Vector3Value;
    }

    /// <summary>
    /// 自動で変換
    /// </summary>
    public static implicit operator MinigameValue(int value) => new MinigameValue { Type = DataType.Int, IntValue = value };
    public static implicit operator MinigameValue(float value) => new MinigameValue { Type = DataType.Float, FloatValue = value };
    public static implicit operator MinigameValue(bool value) => new MinigameValue { Type = DataType.Bool, BoolValue = value };
    public static implicit operator MinigameValue(string value) => new MinigameValue { Type = DataType.Text, TextValue = new FixedString64Bytes(value) };
    public static implicit operator MinigameValue(Vector3 value) => new MinigameValue { Type = DataType.Vector3, Vector3Value = value };


    /// <summary>
    /// value.ToString()を呼ぶだけで中身を文字にできる
    /// </summary>
    public override string ToString()
    {
        switch (Type)
        {
            case DataType.Int:
                {
                    return IntValue.ToString();
                }
            case DataType.Float:
                {
                    return FloatValue.ToString();
                }
            case DataType.Bool:
                {
                    return BoolValue.ToString();
                }
            case DataType.Text:
                {
                    return TextValue.ToString();
                }
            case DataType.Vector3:
                {
                    return Vector3Value.ToString();
                }
            default:
                {
                    return "";
                }
        }
    }
}
