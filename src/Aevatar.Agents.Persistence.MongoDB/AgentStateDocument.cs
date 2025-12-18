using System;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB document for agent state storage
/// Uses Protobuf byte[] for state data to ensure consistency with EventStore
/// 
/// Benefits of byte[] storage:
/// - Consistent serialization with Events (both use Protobuf)
/// - Smaller storage size (~30-50% compared to BSON)
/// - Full compatibility with complex Protobuf types (RepeatedField, MapField, Timestamp)
/// - Better version evolution support via Protobuf schema compatibility
/// </summary>
internal class AgentStateDocument
{
    /// <summary>
    /// Agent ID (MongoDB _id)
    /// </summary>
    [BsonId]
    public Guid AgentId { get; set; }

    /// <summary>
    /// State data serialized as Protobuf bytes
    /// </summary>
    public byte[] StateData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// State type full name for deserialization
    /// </summary>
    public string StateType { get; set; } = string.Empty;

    /// <summary>
    /// Version for optimistic concurrency (event version at snapshot time)
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
