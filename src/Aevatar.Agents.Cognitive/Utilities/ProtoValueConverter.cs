using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Cognitive.Utilities;

// ============================================================
//  Protobuf Value 转换器
//  职责：在 C# 对象和 Protobuf Value 之间转换
// ============================================================

/// <summary>
/// Protobuf Value 转换工具。
/// </summary>
public static class ProtoValueConverter
{
    /// <summary>
    /// 将 Protobuf Value 转换为 C# 对象。
    /// </summary>
    public static object FromProto(Value value) => value.KindCase switch
    {
        Value.KindOneofCase.NullValue => null!,
        Value.KindOneofCase.NumberValue => value.NumberValue,
        Value.KindOneofCase.StringValue => value.StringValue,
        Value.KindOneofCase.BoolValue => value.BoolValue,
        Value.KindOneofCase.StructValue => FromProtoStruct(value.StructValue),
        Value.KindOneofCase.ListValue => value.ListValue.Values.Select(FromProto).ToList(),
        _ => value.ToString()
    };
    
    /// <summary>
    /// 将 Protobuf Struct 转换为 Dictionary。
    /// </summary>
    public static Dictionary<string, object> FromProtoStruct(Struct protoStruct)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, value) in protoStruct.Fields)
        {
            result[key] = FromProto(value);
        }
        return result;
    }
    
    /// <summary>
    /// 将 C# 对象转换为 Protobuf Value。
    /// </summary>
    public static Value ToProto(object? value) => value switch
    {
        null => Value.ForNull(),
        bool b => Value.ForBool(b),
        int i => Value.ForNumber(i),
        long l => Value.ForNumber(l),
        float f => Value.ForNumber(f),
        double d => Value.ForNumber(d),
        string s => Value.ForString(s),
        IEnumerable<object> list => Value.ForList(list.Select(ToProto).ToArray()),
        IDictionary<string, object> dict => ToProtoStruct(dict),
        _ => Value.ForString(value.ToString() ?? "")
    };
    
    /// <summary>
    /// 将 Dictionary 转换为 Protobuf Value (Struct)。
    /// </summary>
    public static Value ToProtoStruct(IDictionary<string, object> dict)
    {
        var structValue = new Struct();
        foreach (var (key, value) in dict)
        {
            structValue.Fields[key] = ToProto(value);
        }
        return Value.ForStruct(structValue);
    }
}
