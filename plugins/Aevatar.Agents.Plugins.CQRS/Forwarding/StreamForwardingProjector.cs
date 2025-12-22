using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS.Forwarding;

/// <summary>
/// Stream forwarding implementation of IStateProjector.
/// Forwards state changes to a message stream using IMessageStreamProvider abstraction.
/// The actual stream implementation (Orleans Stream, MassTransit, etc.) is determined by DI configuration.
/// </summary>
public class StreamForwardingProjector : IStateProjector
{
    private readonly IMessageStreamProvider _streamProvider;
    private readonly ILogger<StreamForwardingProjector> _logger;

    /// <summary>
    /// Stream category for state projection.
    /// Consumers should subscribe to this category to receive state updates.
    /// </summary>
    public const string StateProjectionCategory = "StateProjection";

    public StreamForwardingProjector(
        IMessageStreamProvider streamProvider,
        ILogger<StreamForwardingProjector> logger)
    {
        _streamProvider = streamProvider;
        _logger = logger;
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        try
        {
            // Validate agent ID
            if (string.IsNullOrEmpty(wrapper.AgentId))
            {
                _logger.LogWarning(
                    "Invalid agent ID format: {AgentId}, cannot forward to stream",
                    wrapper.AgentId);
                return;
            }

            // Get stream using the abstraction
            // The actual implementation depends on DI configuration:
            // - LocalMessageStreamProvider (in-memory/Orleans)
            // - MassTransitMessageStreamProvider (RabbitMQ/Kafka)
            var stream = _streamProvider.GetStream(wrapper.AgentId, StateProjectionCategory);

            // Produce the state wrapper to the stream
            await stream.ProduceAsync(wrapper, ct);

            _logger.LogDebug(
                "Forwarded state to stream for {AgentId} (Type: {AgentType}, Version: {Version})",
                wrapper.AgentId, wrapper.AgentType, wrapper.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error forwarding state to stream for {AgentId}",
                wrapper.AgentId);
            throw;
        }
    }
}

