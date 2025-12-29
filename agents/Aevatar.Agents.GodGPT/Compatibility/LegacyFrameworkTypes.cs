// Compatibility layer for legacy Aevatar framework types
// NOTE: Unused types removed. Only keeping types that are actually being used.

using Orleans;

// ============================================================================
// Aevatar.Core.Abstractions namespace
// ============================================================================
namespace Aevatar.Core.Abstractions
{
    // NOTE: StateBase removed - GodChatState deleted, all agents use Protobuf state

    /// <summary>
    /// Legacy event log base class for event sourcing
    /// Used by GodChatEventLog, ChatManageEventLog, AwakeningLogEvent
    /// </summary>
    [GenerateSerializer]
    public abstract class StateLogEventBase<TEventLog> where TEventLog : StateLogEventBase<TEventLog>
    {
    }

    /// <summary>
    /// Legacy event base class
    /// Used by AIStreamingErrorResponseGEvent, RenameChatTitleEvent
    /// </summary>
    [GenerateSerializer]
    public abstract class EventBase
    {
    }
    
    /// <summary>
    /// Legacy IGAgent interface
    /// Referenced via GlobalUsings.cs alias
    /// </summary>
    public interface IGAgent : IGrainWithGuidKey
    {
        Task<string> GetDescriptionAsync();
    }
}

// ============================================================================
// Aevatar.Core namespace
// ============================================================================
namespace Aevatar.Core
{
    /// <summary>
    /// Legacy GAgent attribute for marking GAgent classes
    /// Used by 13 agents in the codebase
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class GAgentAttribute : Attribute
    {
        public string Name { get; }
        
        public GAgentAttribute(string name)
        {
            Name = name;
        }
        
        // Parameterless constructor for compatibility
        public GAgentAttribute() : this(string.Empty)
        {
        }
    }
    
    // NOTE: EventHandlerAttribute was removed - use new framework's EventHandlerAttribute
    // NOTE: GAgentBase<TState, TEventLog> was removed - all agents now use new framework's GAgentBase
}

// ============================================================================
// Aevatar.GAgents.AI.Abstractions namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Abstractions
{
    /// <summary>
    /// Legacy chat message type - matches old framework exactly
    /// Widely used across the codebase
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
    
    // NOTE: AIGAgentStateBase was removed - unused
}

// NOTE: ChatGAgentState namespace removed - GodChatState deleted, all agents use Protobuf state
