using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS;

/// <summary>
/// Shared converter for transforming StateWrapper to StateIndexDocument.
/// Used by both ElasticsearchStateProjector and BatchedStateProjector.
/// </summary>
public class StateDocumentConverter
{
    private readonly ILogger _logger;
    
    // Type cache: TypeUrl -> Type (more precise than AgentType)
    private readonly ConcurrentDictionary<string, Type?> _typeCache = new();
    
    // Pre-built index of all IMessage types for fast lookup
    private static readonly Lazy<Dictionary<string, Type>> _messageTypeIndex = new(BuildMessageTypeIndex);

    public StateDocumentConverter(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Build index of all IMessage types at startup (once per AppDomain)
    /// </summary>
    private static Dictionary<string, Type> BuildMessageTypeIndex()
    {
        var index = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (!typeof(IMessage).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                        continue;
                    
                    // Get Protobuf descriptor to get the full name
                    var descriptorProperty = type.GetProperty("Descriptor", 
                        BindingFlags.Public | BindingFlags.Static);
                    
                    if (descriptorProperty?.GetValue(null) is MessageDescriptor descriptor)
                    {
                        // Index by Protobuf full name (e.g., "aevatar.payment.PaymentIndexState")
                        if (!string.IsNullOrEmpty(descriptor.FullName))
                        {
                            index[descriptor.FullName] = type;
                        }
                    }
                    
                    // Also index by C# type name and full name for fallback
                    index[type.Name] = type;
                    if (type.FullName != null)
                    {
                        index[type.FullName] = type;
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }
        
        return index;
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
    /// Resolve the state type from agent type and state data.
    /// Uses pre-built type index for O(1) lookup instead of scanning all assemblies.
    /// </summary>
    public Type? ResolveStateType(string agentType, Google.Protobuf.WellKnownTypes.Any? stateData)
    {
        if (stateData == null)
            return null;

        var typeUrl = stateData.TypeUrl;
        if (string.IsNullOrEmpty(typeUrl))
            return null;

        // Use TypeUrl as cache key (more precise than AgentType)
        if (_typeCache.TryGetValue(typeUrl, out var cachedType))
            return cachedType;

        // Parse TypeUrl: type.googleapis.com/package.TypeName -> package.TypeName
        var protobufFullName = typeUrl.Contains('/')
            ? typeUrl.Substring(typeUrl.LastIndexOf('/') + 1)
            : typeUrl;

        // Fast O(1) lookup from pre-built index
        Type? type = null;
        var index = _messageTypeIndex.Value;
        
        // Try exact Protobuf full name match first (most reliable)
        if (index.TryGetValue(protobufFullName, out type))
        {
            _typeCache[typeUrl] = type;
            return type;
        }
        
        // Try just the type name (after last dot)
        var simpleTypeName = protobufFullName.Contains('.')
            ? protobufFullName.Substring(protobufFullName.LastIndexOf('.') + 1)
            : protobufFullName;
            
        if (index.TryGetValue(simpleTypeName, out type))
        {
            _typeCache[typeUrl] = type;
            return type;
        }

        // Cache negative result to avoid repeated lookups
        _typeCache[typeUrl] = null;
        
        _logger.LogWarning(
            "Could not resolve state type for {AgentType} from TypeUrl: {TypeUrl}. " +
            "Ensure the Protobuf type is registered in the application.",
            agentType, typeUrl);

        return null;
    }

    private IMessage? UnpackState(Google.Protobuf.WellKnownTypes.Any? stateData, Type? stateType)
    {
        if (stateData == null || stateType == null || !typeof(IMessage).IsAssignableFrom(stateType))
            return null;

        try
        {
            var parserProperty = stateType.GetProperty("Parser",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (parserProperty == null)
            {
                _logger.LogWarning("No Parser property found on type {TypeName}", stateType.Name);
                return null;
            }

            var parser = parserProperty.GetValue(null) as MessageParser;
            if (parser == null)
            {
                _logger.LogWarning("Parser is null for type {TypeName}", stateType.Name);
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
            if (property.Name == "Parser" ||
                property.Name == "Descriptor" ||
                property.Name == "MessageType" ||
                property.DeclaringType == typeof(IMessage) ||
                property.DeclaringType == typeof(object))
                continue;

            try
            {
                var value = property.GetValue(state);
                if (value == null)
                    continue;

                var propertyName = ToCamelCase(property.Name);

                if (IsBasicType(property.PropertyType))
                {
                    // Handle Timestamp specially
                    if (value is Google.Protobuf.WellKnownTypes.Timestamp timestamp)
                    {
                        data[propertyName] = timestamp.ToDateTime();
                    }
                    else
                    {
                        data[propertyName] = value;
                    }
                }
                else
                {
                    // Complex types: serialize to JSON using System.Text.Json
                    data[propertyName] = JsonSerializer.Serialize(value, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract property {PropertyName} from state", property.Name);
            }
        }
    }

    private static bool IsBasicType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsPrimitive)
            return true;

        if (underlyingType == typeof(string) ||
            underlyingType == typeof(DateTime) ||
            underlyingType == typeof(DateTimeOffset) ||
            underlyingType == typeof(decimal) ||
            underlyingType == typeof(Guid) ||
            underlyingType == typeof(Google.Protobuf.WellKnownTypes.Timestamp))
            return true;

        return false;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    #endregion
}

