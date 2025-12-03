// Additional compatibility types for legacy GodGPT framework

using Orleans;
using System.Net.Http;

// ============================================================================
// Aevatar.AI.Feature.StreamSyncWoker namespace
// ============================================================================
namespace Aevatar.AI.Feature.StreamSyncWoker
{
    /// <summary>
    /// Legacy stream sync worker interface
    /// </summary>
    public interface IStreamSyncWorker
    {
        Task<bool> SendStreamChunkAsync(string content, bool isComplete);
    }
}

// ============================================================================
// Aevatar.GAgents.AIGAgent.Agent namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent.Agent
{
    /// <summary>
    /// Legacy AI GAgent interface
    /// </summary>
    public interface IAIGAgent : Aevatar.Core.Abstractions.IGAgent
    {
        Task InitializeAsync(Aevatar.GAgents.AIGAgent.Dtos.InitializeDto initDto);
        Task<List<Aevatar.GAgents.AI.Abstractions.ChatMessage>?> ChatAsync(string prompt);
    }
}

// ============================================================================
// Aevatar.GAgents.AIGAgent.GEvents namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent.GEvents
{
    /// <summary>
    /// Legacy AI streaming error response event - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class AIStreamingErrorResponseGEvent : Aevatar.Core.Abstractions.EventBase
    {
        [Id(0)] public string ErrorMessage { get; set; } = string.Empty;
        [Id(1)] public Aevatar.GAgents.AI.Common.AIExceptionEnum ErrorType { get; set; }
        [Id(2)] public Aevatar.GAgents.AIGAgent.Dtos.AIChatContextDto? Context { get; set; }
    }
}

// ============================================================================
// Aevatar.GAgents.ChatAgent.GAgent namespace
// ============================================================================
namespace Aevatar.GAgents.ChatAgent.GAgent
{
    /// <summary>
    /// Legacy Chat GAgent interface
    /// </summary>
    public interface IChatGAgent : Aevatar.Core.Abstractions.IGAgent
    {
        Task<List<Aevatar.GAgents.AI.Abstractions.ChatMessage>> GetHistoryAsync();
        Task ClearHistoryAsync();
    }
}

// ============================================================================
// Google Calendar DTOs (stub for excluded GoogleAuth)
// ============================================================================
namespace Aevatar.Application.Grains.GoogleAuth.Dtos
{
    /// <summary>
    /// Stub for Google Calendar event DTO - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class GoogleCalendarEventDto
    {
        [Id(0)] public string? Id { get; set; }
        [Id(1)] public string? Summary { get; set; }
        [Id(2)] public string? Description { get; set; }
        [Id(3)] public DateTime? Start { get; set; }
        [Id(4)] public DateTime? End { get; set; }
        [Id(5)] public string? Location { get; set; }
        [Id(6)] public DateTime? StartTime { get; set; }
        [Id(7)] public DateTime? EndTime { get; set; }
    }

    /// <summary>
    /// Stub for Google Calendar list DTO - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class GoogleCalendarListDto
    {
        [Id(0)] public List<GoogleCalendarEventDto> Events { get; set; } = new();
        [Id(1)] public int TotalCount { get; set; }
        [Id(2)] public bool Success { get; set; }
        [Id(3)] public string? Error { get; set; }
    }
    
    /// <summary>
    /// Stub for Google Calendar query DTO
    /// </summary>
    [GenerateSerializer]
    public class GoogleCalendarQueryDto
    {
        [Id(0)] public DateTime? StartDate { get; set; }
        [Id(1)] public DateTime? EndDate { get; set; }
        [Id(2)] public int MaxResults { get; set; } = 10;
    }
}

// ============================================================================
// Chat Config DTO
// ============================================================================
namespace Aevatar.GAgents.ChatAgent.Dtos
{
    /// <summary>
    /// Legacy chat config DTO - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class ChatConfigDto
    {
        [Id(0)] public string? SystemPrompt { get; set; }
        [Id(1)] public int MaxHistoryCount { get; set; } = 50;
        [Id(2)] public bool EnableStreaming { get; set; } = true;
        [Id(3)] public Aevatar.GAgents.AIGAgent.Dtos.LLMConfigDto? LLMConfig { get; set; }
        [Id(4)] public string? Instructions { get; set; }
        [Id(5)] public bool StreamingModeEnabled { get; set; } = true;
        [Id(6)] public Aevatar.GAgents.AI.Options.StreamingConfig? StreamingConfig { get; set; }
    }
}

// ============================================================================
// Event Wrapper Types
// ============================================================================
namespace Aevatar.Core
{
    /// <summary>
    /// Base class for event wrappers
    /// </summary>
    [GenerateSerializer]
    public abstract class EventWrapperBase
    {
        [Id(0)] public Guid EventId { get; set; }
        [Id(1)] public Orleans.Runtime.GrainId SourceGrainId { get; set; }
        [Id(2)] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Generic event wrapper
    /// </summary>
    [GenerateSerializer]
    public class EventWrapper<T> : EventWrapperBase where T : class
    {
        [Id(3)] public T Event { get; set; }
        
        public EventWrapper(T @event, Guid eventId, Orleans.Runtime.GrainId sourceGrainId)
        {
            Event = @event;
            EventId = eventId;
            SourceGrainId = sourceGrainId;
        }
        
        // Parameterless constructor for serialization
        public EventWrapper()
        {
            Event = default!;
        }
    }
}

