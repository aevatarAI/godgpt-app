using System.Text.Json;
using Google.Protobuf;

namespace Aevatar.Agents.Persistence.Supabase.Internal;

/// <summary>
/// 配置对象的 JSON 序列化策略：
///
/// - 若配置类型是 Protobuf（实现 <see cref="IMessage"/>），则使用 Protobuf JSON 映射（稳定、可演进）
/// - 否则退化为 System.Text.Json（兼容非 Protobuf 的本地配置对象）
///
/// 注意：框架规范要求跨边界配置对象必须是 Protobuf，本类因此把 Protobuf 作为第一优先级。
/// </summary>
internal static class SupabaseConfigJson
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    internal static string Serialize<TConfig>(TConfig config)
        where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config is IMessage msg)
        {
            // Protobuf JSON：可保持字段名/默认值处理符合 Protobuf 语义
            return JsonFormatter.Default.Format(msg);
        }

        return JsonSerializer.Serialize(config, DefaultJsonOptions);
    }

    internal static TConfig? Deserialize<TConfig>(string json)
        where TConfig : class, new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        // Protobuf JSON：使用 MessageDescriptor 解析（避免要求 TConfig 在编译期带 IMessage 约束）
        var instance = new TConfig();
        if (instance is IMessage msg)
        {
            var parsed = JsonParser.Default.Parse(json, msg.Descriptor);
            return (TConfig)parsed;
        }

        return JsonSerializer.Deserialize<TConfig>(json, DefaultJsonOptions);
    }
}


