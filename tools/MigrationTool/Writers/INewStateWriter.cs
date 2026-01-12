using Google.Protobuf;

namespace MigrationTool.Writers;

/// <summary>
/// Interface for writing new Protobuf state to MongoDB
/// </summary>
/// <typeparam name="TNewState">New Protobuf state type</typeparam>
public interface INewStateWriter<TNewState>
    where TNewState : IMessage<TNewState>, new()
{
    /// <summary>
    /// Write state to new storage
    /// </summary>
    Task WriteAsync(string agentId, TNewState state);
    
    /// <summary>
    /// Read state from new storage (for verification)
    /// </summary>
    Task<TNewState?> ReadAsync(string agentId);
    
    /// <summary>
    /// Check if state exists
    /// </summary>
    Task<bool> ExistsAsync(string agentId);
    
    /// <summary>
    /// Delete state (for rollback)
    /// </summary>
    Task DeleteAsync(string agentId);
}
