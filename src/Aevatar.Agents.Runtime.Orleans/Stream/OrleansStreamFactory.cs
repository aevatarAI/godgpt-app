using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.Stream;

/// <summary>
/// Factory for creating unified IMessageStream instances in Orleans Runtime.
/// Abstracts away the difference between Orleans Stream and MassTransit Stream.
/// </summary>
public class OrleansStreamFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrleansStreamFactory> _logger;
    private readonly IOptions<MessageStreamProviderOptions>? _providerOptions;
    private readonly IMessageStreamProvider? _externalStreamProvider;
    private readonly IOptions<StreamingOptions>? _streamingOptions;

    public OrleansStreamFactory(
        IServiceProvider serviceProvider,
        ILogger<OrleansStreamFactory> logger,
        IOptions<MessageStreamProviderOptions>? providerOptions = null,
        IMessageStreamProvider? externalStreamProvider = null,
        IOptions<StreamingOptions>? streamingOptions = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _providerOptions = providerOptions;
        _externalStreamProvider = externalStreamProvider;
        _streamingOptions = streamingOptions;
    }

    /// <summary>
    /// Create a unified IMessageStream for the given agent.
    /// Automatically selects between Orleans Stream and MassTransit Stream based on configuration.
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="agentCategory">Agent category (for MassTransit topic routing)</param>
    /// <param name="getStreamProvider">Function to get Orleans StreamProvider (from Grain)</param>
    public Task<IMessageStream> CreateStreamAsync(
        string agentId,
        string? agentCategory = null,
        Func<string, IStreamProvider>? getStreamProvider = null)
    {
        // Determine provider type
        var providerType = DetermineProviderType();

        if (providerType == "MassTransit" && _externalStreamProvider != null)
        {
            _logger.LogDebug("Creating MassTransit stream for Agent {AgentId}, Category: {Category}", 
                agentId, agentCategory ?? "null");
            return Task.FromResult(_externalStreamProvider.GetStream(agentId, agentCategory));
        }
        else
        {
            // Use Orleans Stream (default)
            return CreateOrleansStreamAsync(agentId, getStreamProvider);
        }
    }

    /// <summary>
    /// Create Orleans Stream wrapped as IMessageStream.
    /// </summary>
    private Task<IMessageStream> CreateOrleansStreamAsync(
        string agentId,
        Func<string, IStreamProvider>? getStreamProvider)
    {
        var streamNamespace = _streamingOptions?.Value?.DefaultStreamNamespace 
            ?? AevatarAgentsOrleansConstants.StreamNamespace;
        var streamProviderName = _streamingOptions?.Value?.StreamProviderName 
            ?? AevatarAgentsOrleansConstants.StreamProviderName;

        if (getStreamProvider == null)
        {
            throw new InvalidOperationException(
                "GetStreamProvider function is required for Orleans Stream creation");
        }

        var streamProvider = getStreamProvider(streamProviderName);
        var streamId = StreamId.Create(streamNamespace, agentId);
        var orleansStream = streamProvider.GetStream<byte[]>(streamId);

        _logger.LogDebug("Created Orleans stream for Agent {AgentId}, Namespace: {Namespace}", 
            agentId, streamNamespace);

        return Task.FromResult<IMessageStream>(new OrleansMessageStream(agentId, orleansStream));
    }

    /// <summary>
    /// Determine which stream provider to use based on configuration.
    /// </summary>
    public string DetermineProviderType()
    {
        var providerType = _providerOptions?.Value?.Provider ?? "Default";
        
        if (_providerOptions?.Value?.Runtime != null && 
            _providerOptions.Value.Runtime.TryGetValue("Orleans", out var runtimeProvider))
        {
            providerType = runtimeProvider;
        }

        return providerType;
    }

    /// <summary>
    /// Check if the stream provider requires agent category for topic routing.
    /// Currently only MassTransit requires category.
    /// </summary>
    public bool RequiresCategoryForStream()
    {
        return DetermineProviderType() == "MassTransit";
    }
}

