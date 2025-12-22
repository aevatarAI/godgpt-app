using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.Persistence;

namespace Aevatar.Agents.Core.Persistence;

/// <summary>
/// In-memory configuration store implementation
/// Thread-safe using ConcurrentDictionary with composite keys (AgentType:AgentId)
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
public class InMemoryConfigStore<TConfig> : IConfigStore<TConfig>
    where TConfig : class, new()
{
    // Use composite key: "FullTypeName:AgentId" to isolate configs by agent type
    private readonly ConcurrentDictionary<string, TConfig> _configs = new();

    /// <summary>
    /// Create composite key from agent type and ID
    /// </summary>
    private static string CreateKey(Type agentType, string agentId)
    {
        return $"{agentType.FullName}:{agentId}";
    }

    /// <summary>
    /// Load configuration from memory
    /// </summary>
    public Task<TConfig?> LoadAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        var key = CreateKey(agentType, agentId);
        _configs.TryGetValue(key, out var config);
        return Task.FromResult(config);
    }

    /// <summary>
    /// Save configuration to memory
    /// </summary>
    public Task SaveAsync(Type agentType, string agentId, TConfig config, CancellationToken ct = default)
    {
        var key = CreateKey(agentType, agentId);
        _configs[key] = config;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete configuration from memory
    /// </summary>
    public Task DeleteAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        var key = CreateKey(agentType, agentId);
        _configs.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if configuration exists
    /// </summary>
    public Task<bool> ExistsAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        var key = CreateKey(agentType, agentId);
        return Task.FromResult(_configs.ContainsKey(key));
    }

    /// <summary>
    /// Get all stored configurations (for testing/debugging)
    /// Returns dictionary with composite keys (AgentType:AgentId)
    /// </summary>
    public IReadOnlyDictionary<string, TConfig> GetAllConfigs()
    {
        return _configs;
    }

    /// <summary>
    /// Get configurations for a specific agent type (for testing/debugging)
    /// </summary>
    public Dictionary<Guid, TConfig> GetConfigsByAgentType(Type agentType)
    {
        var prefix = $"{agentType.FullName}:";
        var result = new Dictionary<Guid, TConfig>();
        
        foreach (var kvp in _configs)
        {
            if (kvp.Key.StartsWith(prefix))
            {
                var idString = kvp.Key.Substring(prefix.Length);
                if (Guid.TryParse(idString, out var agentId))
                {
                    result[agentId] = kvp.Value;
                }
            }
        }
        
        return result;
    }

    /// <summary>
    /// Clear all configurations (for testing)
    /// </summary>
    public void Clear()
    {
        _configs.Clear();
    }
}



