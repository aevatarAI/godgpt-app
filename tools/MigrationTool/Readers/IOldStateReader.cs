namespace MigrationTool.Readers;

/// <summary>
/// Interface for reading old state from Orleans Grain Storage
/// </summary>
/// <typeparam name="TOldState">Old state type</typeparam>
public interface IOldStateReader<TOldState>
{
    /// <summary>
    /// Read a single agent's state
    /// </summary>
    Task<TOldState?> ReadAsync(string agentId);
    
    /// <summary>
    /// Read all agents' states
    /// </summary>
    IAsyncEnumerable<(string AgentId, TOldState State)> ReadAllAsync();
    
    /// <summary>
    /// Get total count of agents
    /// </summary>
    Task<int> GetCountAsync();
    
    /// <summary>
    /// Get a sample of agent IDs for verification
    /// </summary>
    Task<List<string>> GetSampleIdsAsync(int count);
}
