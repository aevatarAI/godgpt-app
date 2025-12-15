using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS;

/// <summary>
/// Shared converter for transforming StateWrapper to StateIndexDocument.
/// Used by both ElasticsearchStateProjector and BatchedStateProjector.
/// </summary>
public class StateDocumentConverter
{
    private readonly ILogger _logger;
    
    // Core caches (only what's necessary)
    private static readonly ConcurrentDictionary<string, Type?> _typeCache = new();
    private static readonly ConcurrentDictionary<Type, MessageParser?> _parserCache = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public StateDocumentConverter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Convert StateWrapper to StateIndexDocument with full state unpacking.
    /// </summary>
    public StateIndexDocument Convert(StateWrapper wrapper)
    {
        var data = new Dictionary<string, object>();

        // Try to resolve and unpack state
        var stateType = ResolveStateType(wrapper.AgentType, wrapper.StateData);
        var state = UnpackState(wrapper.StateData, stateType);

        if (state != null && stateType != null)
        {
            ExtractStateProperties(state, stateType, data);
        }

        return new StateIndexDocument
        {
            AgentId = wrapper.AgentId,
            AgentType = wrapper.AgentType,
            Data = data,
            Version = wrapper.Version,
            IndexedAt = wrapper.PublishedAt?.ToDateTime() ?? DateTime.UtcNow
        };
    }

    #region State Resolution

    /// <summary>
    /// Resolve the state type from TypeUrl with caching.
    /// </summary>
    public Type? ResolveStateType(string agentType, Google.Protobuf.WellKnownTypes.Any? stateData)
    {
        if (stateData == null || string.IsNullOrEmpty(stateData.TypeUrl))
            return null;

        var typeUrl = stateData.TypeUrl;
        
        // Check cache first
        if (_typeCache.TryGetValue(typeUrl, out var cachedType))
            return cachedType;

        // Parse TypeUrl: type.googleapis.com/package.TypeName -> TypeName
        var typeName = typeUrl.Contains('/')
            ? typeUrl.Substring(typeUrl.LastIndexOf('/') + 1)
            : typeUrl;
        
        // Simple type name (after last dot)
        var simpleName = typeName.Contains('.')
            ? typeName.Substring(typeName.LastIndexOf('.') + 1)
            : typeName;

        // Search for type (only runs once per TypeUrl due to caching)
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t => 
                typeof(IMessage).IsAssignableFrom(t) && 
                !t.IsAbstract && 
                (t.Name == simpleName || t.FullName == typeName));

        // Cache result (including null for negative caching)
        _typeCache[typeUrl] = type;
        
        if (type == null)
        {
            _logger.LogWarning("Could not resolve type for {AgentType} from {TypeUrl}", agentType, typeUrl);
        }

        return type;
    }

    private IMessage? UnpackState(Google.Protobuf.WellKnownTypes.Any? stateData, Type? stateType)
    {
        if (stateData == null || stateType == null || !typeof(IMessage).IsAssignableFrom(stateType))
            return null;

        try
        {
            // Get parser from cache or resolve once
            var parser = _parserCache.GetOrAdd(stateType, type =>
            {
                var parserProperty = type.GetProperty("Parser",
                    BindingFlags.Public | BindingFlags.Static);
                return parserProperty?.GetValue(null) as MessageParser;
            });

            if (parser == null)
            {
                _logger.LogWarning("No Parser found for type {TypeName}", stateType.Name);
                return null;
            }

            return parser.ParseFrom(stateData.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpack state of type {TypeName}", stateType?.Name);
            return null;
        }
    }

    #endregion

    #region Property Extraction

    private void ExtractStateProperties(IMessage state, Type stateType, Dictionary<string, object> data)
    {
        foreach (var property in stateType.GetProperties())
        {
            // Skip Protobuf internal properties
            if (property.Name is "Parser" or "Descriptor" or "MessageType" ||
                property.DeclaringType == typeof(IMessage) ||
                property.DeclaringType == typeof(object))
                continue;

            try
            {
                var value = property.GetValue(state);
                if (value == null) continue;

                var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..]; // camelCase

                data[name] = value switch
                {
                    Google.Protobuf.WellKnownTypes.Timestamp ts => ts.ToDateTime(),
                    _ when IsBasicType(property.PropertyType) => value,
                    _ => JsonSerializer.Serialize(value, _jsonOptions)
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract property {Property}", property.Name);
            }
        }
    }

    private static bool IsBasicType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t == typeof(string) || t == typeof(DateTime) || 
               t == typeof(decimal) || t == typeof(Guid) || 
               t == typeof(Google.Protobuf.WellKnownTypes.Timestamp);
    }

    #endregion
}

