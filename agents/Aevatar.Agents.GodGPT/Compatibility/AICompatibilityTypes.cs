// AI Compatibility Types for legacy GodGPT framework
// NOTE: This file only contains types that are actually being used.
// Unused types have been removed to reduce confusion.

using Orleans;

// ============================================================================
// Aevatar.GAgents.AI.Common namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Common
{
    // NOTE: ResponseToPublisherEventBase was removed
    // ResponseCreateGod and ResponseStreamGodChat now include base fields directly

    /// <summary>
    /// Legacy AI exception enum - matches old framework exactly
    /// Widely used across the codebase
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
    /// Legacy AI stream chat content - matches old framework exactly
    /// Internal use only, converted to Proto before RPC
    /// </summary>
    public class AIStreamChatContent
    {
        [Id(0)] public string Content { get; set; } = string.Empty;
        [Id(1)] public bool IsComplete { get; set; }
        [Id(2)] public int TokenCount { get; set; }
        [Id(3)] public string? Error { get; set; }
        [Id(4)] public bool IsLastChunk { get; set; }
        [Id(5)] public string? ResponseContent { get; set; }
        [Id(6)] public string? AggregationMsg { get; set; }
        [Id(7)] public int SerialNumber { get; set; }
        [Id(8)] public bool IsAggregationMsg { get; set; }
    }
}

// ============================================================================
// Aevatar.GAgents.AI.Options namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Options
{
    /// <summary>
    /// Legacy execution prompt settings
    /// Internal use only, converted to Proto before RPC
    /// NOTE: Interface methods use null default, actual values converted internally
    /// </summary>
    public class ExecutionPromptSettings
    {
        [Id(0)] public string? Temperature { get; set; }
        [Id(1)] public string? MaxTokens { get; set; }
        [Id(2)] public string? TopP { get; set; }
        [Id(3)] public string? FrequencyPenalty { get; set; }
        [Id(4)] public string? PresencePenalty { get; set; }
        [Id(5)] public List<string>? StopSequences { get; set; }
        [Id(6)] public string? Model { get; set; }
    }
    
    // NOTE: StreamingConfig was removed - unused
}

// ============================================================================
// Aevatar.GAgents.AIGAgent.Dtos namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent.Dtos
{
    /// <summary>
    /// Legacy AI chat context DTO - matches old framework exactly
    /// Internal use only, converted to Proto before RPC
    /// </summary>
    public class AIChatContextDto
    {
        [Id(0)] public string? AgentId { get; set; }
        [Id(1)] public string? SessionId { get; set; }
        [Id(2)] public string? UserId { get; set; }
        [Id(3)] public Dictionary<string, string>? Metadata { get; set; }
        [Id(4)] public string? SystemPrompt { get; set; }
        [Id(5)] public List<Aevatar.GAgents.AI.Abstractions.ChatMessage>? History { get; set; }
        [Id(6)] public Guid RequestId { get; set; }
        [Id(7)] public string? ChatId { get; set; }
        [Id(8)] public string? MessageId { get; set; }
    }
}

// ============================================================================
// Aevatar.GAgents.ChatAgent.Dtos namespace
// ============================================================================
namespace Aevatar.GAgents.ChatAgent.Dtos
{
    /// <summary>
    /// Legacy chat role enum
    /// Widely used across the codebase
    /// </summary>
    public enum ChatRole
    {
        System = 0,
        User = 1,
        Assistant = 2,
        Tool = 3
    }
}

// NOTE: AIException was removed - unused
// NOTE: ConfigurationBase was removed - ManagerConfigDto now includes fields directly
