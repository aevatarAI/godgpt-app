// AI Compatibility Types for legacy GodGPT framework
// These types bridge the old MyGet-based AI framework to the new Aevatar.Agents.AI framework

using Orleans;

// ============================================================================
// Aevatar.GAgents.AI.Common namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Common
{
    /// <summary>
    /// Legacy response to publisher event base - used for SignalR responses
    /// </summary>
    [GenerateSerializer]
    public abstract class ResponseToPublisherEventBase
    {
        [Id(0)] public string? CorrelationId { get; set; }
        [Id(1)] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Legacy AI exception enum - matches old framework exactly
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
    /// </summary>
    [GenerateSerializer]
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
    /// </summary>
    [GenerateSerializer]
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

    /// <summary>
    /// Legacy streaming config - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class StreamingConfig
    {
        [Id(0)] public bool EnableStreaming { get; set; } = true;
        [Id(1)] public int BufferSize { get; set; } = 1024;
        [Id(2)] public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
        [Id(3)] public int BufferingSize { get; set; } = 1024;
        [Id(4)] public string? StreamNamespace { get; set; }
    }
}

// ============================================================================
// Aevatar.GAgents.AIGAgent.Dtos namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent.Dtos
{
    /// <summary>
    /// Legacy AI chat context DTO - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
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

    /// <summary>
    /// Legacy LLM config DTO - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class LLMConfigDto
    {
        [Id(0)] public string? ModelId { get; set; }
        [Id(1)] public string? Provider { get; set; }
        [Id(2)] public double Temperature { get; set; } = 0.7;
        [Id(3)] public int MaxTokens { get; set; } = 2000;
        [Id(4)] public string? ApiKey { get; set; }
        [Id(5)] public string? Endpoint { get; set; }
        [Id(6)] public string? SystemLLM { get; set; }
    }

    /// <summary>
    /// Legacy initialize DTO - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class InitializeDto
    {
        [Id(0)] public string? SystemPrompt { get; set; }
        [Id(1)] public LLMConfigDto? LLMConfig { get; set; }
        [Id(2)] public Dictionary<string, string>? Metadata { get; set; }
        [Id(3)] public string? Instructions { get; set; }
        [Id(4)] public bool StreamingModeEnabled { get; set; } = true;
        [Id(5)] public Aevatar.GAgents.AI.Options.StreamingConfig? StreamingConfig { get; set; }
    }
}

// ============================================================================
// Aevatar.GAgents.AIGAgent.State namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent.State
{
    /// <summary>
    /// Legacy AI GAgent state base
    /// </summary>
    [GenerateSerializer]
    public class AIGAgentState : Aevatar.GAgents.AI.Abstractions.AIGAgentStateBase
    {
        [Id(10)] public string? CurrentSessionId { get; set; }
        [Id(11)] public string? SystemPrompt { get; set; }
    }
}

// ============================================================================
// Aevatar.GAgents.ChatAgent.Dtos namespace
// ============================================================================
namespace Aevatar.GAgents.ChatAgent.Dtos
{
    /// <summary>
    /// Legacy chat role enum
    /// </summary>
    public enum ChatRole
    {
        System = 0,
        User = 1,
        Assistant = 2,
        Tool = 3
    }
}

// NOTE: Aevatar.AI.Feature.IAIFeature removed - unused

// ============================================================================
// Aevatar.AI.Exceptions namespace
// ============================================================================
namespace Aevatar.AI.Exceptions
{
    /// <summary>
    /// Legacy AI exception
    /// </summary>
    public class AIException : Exception
    {
        public Aevatar.GAgents.AI.Common.AIExceptionEnum ExceptionType { get; }

        public AIException(string message, Aevatar.GAgents.AI.Common.AIExceptionEnum exceptionType = Aevatar.GAgents.AI.Common.AIExceptionEnum.Unknown)
            : base(message)
        {
            ExceptionType = exceptionType;
        }
    }
}

// ============================================================================
// Aevatar.Core.Abstractions - Configuration base
// ============================================================================
namespace Aevatar.Core.Abstractions
{
    /// <summary>
    /// Legacy configuration base class
    /// </summary>
    [GenerateSerializer]
    public abstract class ConfigurationBase
    {
        [Id(0)] public string? ConfigId { get; set; }
        [Id(1)] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [Id(2)] public DateTime? UpdatedAt { get; set; }
    }
}

