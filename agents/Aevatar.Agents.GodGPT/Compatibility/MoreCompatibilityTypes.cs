// Additional compatibility types for legacy GodGPT framework
// NOTE: This file only contains types that are actually being used.

using Orleans;

// ============================================================================
// Aevatar.GAgents.AIGAgent.GEvents namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent.GEvents
{
    /// <summary>
    /// AI streaming error response event - used by ChatManagerGAgent.Handlers.cs
    /// </summary>
    [GenerateSerializer]
    public class AIStreamingErrorResponseGEvent
    {
        [Id(0)] public string ErrorMessage { get; set; } = string.Empty;
        [Id(1)] public Aevatar.GAgents.AI.Common.AIExceptionEnum ErrorType { get; set; }
        [Id(2)] public Aevatar.GAgents.AIGAgent.Dtos.AIChatContextDto? Context { get; set; }
    }
}
