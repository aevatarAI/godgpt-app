// Compatibility layer for legacy Aevatar framework types
// NOTE: ChatMessage is internal DTO, converted to ChatMessageProto before RPC

// ============================================================================
// Aevatar.GAgents.AI.Abstractions namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Abstractions
{
    /// <summary>
    /// Legacy chat message type - internal DTO
    /// Converted to ChatMessageProto before any RPC/serialization
    /// </summary>
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Aevatar.GAgents.ChatAgent.Dtos.ChatRole ChatRole { get; set; }
        public List<string>? ImageKeys { get; set; }
    }
}
