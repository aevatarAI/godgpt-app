// =============================================================================
// Legacy Compatibility Types for GodGPT
// =============================================================================
// These types are internal DTOs, converted to Proto before any RPC/serialization.
// They do NOT need Orleans serialization.

// =============================================================================
// Aevatar.GAgents.AI.Abstractions namespace
// =============================================================================
namespace Aevatar.GAgents.AI.Abstractions
{
    /// <summary>
    /// Legacy chat message - internal DTO
    /// Converted to ChatMessageProto before RPC
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

// =============================================================================
// Aevatar.GAgents.AI.Common namespace
// =============================================================================
namespace Aevatar.GAgents.AI.Common
{
    /// <summary>
    /// AI exception enum - widely used across the codebase
    /// </summary>
    public enum AIExceptionEnum
    {
        None = 0,
        InvalidInput = 1,
        ModelNotFound = 2,
        RateLimitExceeded = 3,
        TokenLimitExceeded = 4,
        ServiceUnavailable = 5,
        AuthenticationFailed = 6,
        RequestLimitError = 7,
        Unknown = 99
    }

    /// <summary>
    /// AI stream chat content - internal DTO
    /// Converted to AIStreamChatContentProto before RPC
    /// </summary>
    public class AIStreamChatContent
    {
        public string Content { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public int TokenCount { get; set; }
        public string? Error { get; set; }
        public bool IsLastChunk { get; set; }
        public string? ResponseContent { get; set; }
        public string? AggregationMsg { get; set; }
        public int SerialNumber { get; set; }
        public bool IsAggregationMsg { get; set; }
        /// <summary>
        /// Extracted conversation suggestions (after [SUGGESTIONS] block filtering)
        /// Only populated when streaming filter extracts suggestions from response
        /// </summary>
        public List<string>? ExtractedSuggestions { get; set; }
    }
}

// =============================================================================
// Aevatar.GAgents.AI.Options namespace
// =============================================================================
namespace Aevatar.GAgents.AI.Options
{
    /// <summary>
    /// Execution prompt settings - internal DTO
    /// Converted to ExecutionPromptSettingsProto before RPC
    /// </summary>
    public class ExecutionPromptSettings
    {
        public string? Temperature { get; set; }
        public string? MaxTokens { get; set; }
        public string? TopP { get; set; }
        public string? FrequencyPenalty { get; set; }
        public string? PresencePenalty { get; set; }
        public List<string>? StopSequences { get; set; }
        public string? Model { get; set; }
    }
}

// =============================================================================
// Aevatar.GAgents.AIGAgent.Dtos namespace
// =============================================================================
namespace Aevatar.GAgents.AIGAgent.Dtos
{
    /// <summary>
    /// AI chat context - internal DTO
    /// Converted to AIChatContextProto before RPC
    /// </summary>
    public class AIChatContextDto
    {
        public string? AgentId { get; set; }
        public string? SessionId { get; set; }
        public string? UserId { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
        public string? SystemPrompt { get; set; }
        public List<Aevatar.GAgents.AI.Abstractions.ChatMessage>? History { get; set; }
        public Guid RequestId { get; set; }
        public string? ChatId { get; set; }
        public string? MessageId { get; set; }
    }
}

// =============================================================================
// Aevatar.GAgents.ChatAgent.Dtos namespace
// =============================================================================
namespace Aevatar.GAgents.ChatAgent.Dtos
{
    /// <summary>
    /// Chat role enum - widely used across the codebase
    /// Note: Values match old Aevatar.GAgents.ChatAgent package for API compatibility
    /// </summary>
    public enum ChatRole
    {
        User = 0,
        Assistant = 1,
        System = 2,
        Tool = 3
    }
}

