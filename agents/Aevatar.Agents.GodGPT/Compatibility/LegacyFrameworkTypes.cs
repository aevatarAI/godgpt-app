// Compatibility layer for legacy Aevatar framework types
// NOTE: Most types removed - only keeping ChatMessage which is widely used (170+)

using Orleans;

// ============================================================================
// Aevatar.GAgents.AI.Abstractions namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Abstractions
{
    /// <summary>
    /// Legacy chat message type - matches old framework exactly
    /// Widely used across the codebase (170+ references)
    /// </summary>
    [GenerateSerializer]
    public class ChatMessage
    {
        [Id(0)] public string Role { get; set; } = string.Empty;
        [Id(1)] public string Content { get; set; } = string.Empty;
        [Id(2)] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        [Id(3)] public Aevatar.GAgents.ChatAgent.Dtos.ChatRole ChatRole { get; set; }
        [Id(4)] public List<string>? ImageKeys { get; set; }
    }
}

// NOTE: All other types removed:
// - StateLogEventBase<T> - all RaiseEvent uses Proto events
// - IGAgent - replaced with Aevatar.Agents.Abstractions.IGAgent
// - GAgentAttribute - runtime never reads it
// - EventBase - no inheritors
// - StateBase, ChatGAgentState - all agents use Protobuf state
