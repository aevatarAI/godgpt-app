using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Orleans;

namespace Aevatar.Agents.GodGPT.Common;

/// <summary>
/// Helper class for agent retrieval patterns during migration.
/// Encapsulates the differences between old GrainFactory and new IGAgentFactory patterns.
/// </summary>
public static class AgentRetrievalHelpers
{
    /// <summary>
    /// Get a new framework agent using IGAgentFactory.
    /// This replaces the old GrainFactory.GetGrain pattern for migrated agents.
    /// </summary>
    /// <example>
    /// // Old pattern:
    /// var agent = GrainFactory.GetGrain&lt;ISomeAgent&gt;(id);
    /// 
    /// // New pattern:
    /// var agent = await agentFactory.GetAgentAsync&lt;SomeAgent&gt;(id);
    /// </example>
    public static async Task<TAgent> GetAgentAsync<TAgent>(
        this IGAgentFactory agentFactory, 
        Guid id) 
        where TAgent : GAgentBase
    {
        var agent = agentFactory.CreateGAgent<TAgent>(id);
        await agent.ActivateAsync();
        return agent;
    }
    
    /// <summary>
    /// Get a legacy Orleans Grain using IClusterClient.
    /// Use this for grains that have NOT been migrated to the new framework.
    /// </summary>
    /// <example>
    /// // For unmigrated grains:
    /// var grain = clusterClient.GetLegacyGrain&lt;ILegacyGrain&gt;(id);
    /// </example>
    public static TGrainInterface GetLegacyGrain<TGrainInterface>(
        this IClusterClient clusterClient,
        Guid id)
        where TGrainInterface : IGrainWithGuidKey
    {
        return clusterClient.GetGrain<TGrainInterface>(id);
    }
    
    /// <summary>
    /// Get a legacy Orleans Grain using IClusterClient with integer key.
    /// </summary>
    public static TGrainInterface GetLegacyGrain<TGrainInterface>(
        this IClusterClient clusterClient,
        long id)
        where TGrainInterface : IGrainWithIntegerKey
    {
        return clusterClient.GetGrain<TGrainInterface>(id);
    }
    
    /// <summary>
    /// Get a legacy Orleans Grain using IClusterClient with string key.
    /// </summary>
    public static TGrainInterface GetLegacyGrain<TGrainInterface>(
        this IClusterClient clusterClient,
        string id)
        where TGrainInterface : IGrainWithStringKey
    {
        return clusterClient.GetGrain<TGrainInterface>(id);
    }
}

