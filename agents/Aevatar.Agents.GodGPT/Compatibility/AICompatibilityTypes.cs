// AI Compatibility Types for legacy GodGPT framework
// NOTE: These types are used internally and converted to Proto before RPC.
// They do NOT need Orleans serialization since they never cross RPC boundaries directly.

// ============================================================================
// Aevatar.GAgents.AI.Common namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Common
{
    /// <summary>
    /// Legacy AI exception enum - widely used across the codebase
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
    /// Legacy AI stream chat content
    /// Internal use only, converted to AIStreamChatContentProto before RPC
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
    }
}

// ============================================================================
// Aevatar.GAgents.AI.Options namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Options
{
    /// <summary>
    /// Legacy execution prompt settings
    /// Internal use only, converted to ExecutionPromptSettingsProto before RPC
    /// NOTE: Interface methods use null default, actual values converted internally
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

// ============================================================================
// Aevatar.GAgents.AIGAgent.Dtos namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent.Dtos
{
    /// <summary>
    /// Legacy AI chat context DTO
    /// Internal use only, converted to AIChatContextProto before RPC
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

// ============================================================================
// Aevatar.GAgents.ChatAgent.Dtos namespace
// ============================================================================
namespace Aevatar.GAgents.ChatAgent.Dtos
{
    /// <summary>
    /// Legacy chat role enum - widely used across the codebase
    /// </summary>
    public enum ChatRole
    {
        System = 0,
        User = 1,
        Assistant = 2,
        Tool = 3
    }
}
