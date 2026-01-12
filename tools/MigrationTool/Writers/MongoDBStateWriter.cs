using Google.Protobuf;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MigrationTool.Writers;

/// <summary>
/// MongoDB state document structure (matches new framework)
/// </summary>
public class AgentStateDocument
{
    [BsonId]
    public string AgentId { get; set; } = string.Empty;
    
    public byte[] StateData { get; set; } = Array.Empty<byte>();
    
    public string StateType { get; set; } = string.Empty;
    
    public long Version { get; set; } = 1;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? MigratedAt { get; set; }
    
    public string? MigrationSource { get; set; }
}

/// <summary>
/// MongoDB state writer for new Protobuf states
/// </summary>
/// <typeparam name="TState">Protobuf state type</typeparam>
public class MongoDBStateWriter<TState> : INewStateWriter<TState>
    where TState : class, IMessage<TState>, new()
{
    private readonly IMongoCollection<AgentStateDocument> _collection;
    private readonly string _stateTypeName;

    public MongoDBStateWriter(IMongoDatabase database, string? collectionName = null)
    {
        var name = collectionName ?? $"agent_states_{typeof(TState).Name}";
        _collection = database.GetCollection<AgentStateDocument>(name);
        _stateTypeName = typeof(TState).FullName ?? typeof(TState).Name;
        
        // Ensure indexes
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var indexKeys = Builders<AgentStateDocument>.IndexKeys
            .Ascending(x => x.StateType)
            .Ascending(x => x.UpdatedAt);
            
        _collection.Indexes.CreateOne(new CreateIndexModel<AgentStateDocument>(indexKeys));
    }

    /// <summary>
    /// Write state to MongoDB
    /// </summary>
    public async Task WriteAsync(string agentId, TState state)
    {
        var doc = new AgentStateDocument
        {
            AgentId = agentId,
            StateData = state.ToByteArray(),
            StateType = _stateTypeName,
            Version = 1,
            UpdatedAt = DateTime.UtcNow,
            MigratedAt = DateTime.UtcNow,
            MigrationSource = "MigrationTool"
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true });
    }

    /// <summary>
    /// Read state from MongoDB
    /// </summary>
    public async Task<TState?> ReadAsync(string agentId)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId).FirstOrDefaultAsync();
        if (doc?.StateData == null || doc.StateData.Length == 0)
            return null;

        var state = new TState();
        state.MergeFrom(doc.StateData);
        return state;
    }

    /// <summary>
    /// Check if state exists
    /// </summary>
    public async Task<bool> ExistsAsync(string agentId)
    {
        var count = await _collection.CountDocumentsAsync(x => x.AgentId == agentId);
        return count > 0;
    }

    /// <summary>
    /// Delete state (for rollback)
    /// </summary>
    public async Task DeleteAsync(string agentId)
    {
        await _collection.DeleteOneAsync(x => x.AgentId == agentId);
    }
}
