using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Persistence;
using Google.Protobuf;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB state store implementation with Protobuf serialization
/// 
/// Architecture:
/// - State is serialized to byte[] using Protobuf (same as Events)
/// - Provides consistent serialization across Events and States
/// - Supports all Protobuf types (RepeatedField, MapField, Timestamp, etc.)
/// </summary>
/// <typeparam name="TState">State type (must be Protobuf IMessage)</typeparam>
public class MongoDBStateStore<TState> : IVersionedStateStore<TState>
    where TState : class, IMessage<TState>, new()
{
    private readonly IMongoCollection<AgentStateDocument> _collection;
    private readonly string _stateTypeName;

    // Static constructor ensures BSON serializers are configured once per type
    static MongoDBStateStore()
    {
        MongoDBServiceCollectionExtensions.ConfigureBsonSerializers();
    }

    /// <summary>
    /// Create MongoDB state store with Protobuf serialization
    /// </summary>
    /// <param name="database">MongoDB database instance</param>
    /// <param name="collectionName">Optional custom collection name (default: agent_states_{StateTypeName})</param>
    public MongoDBStateStore(
        IMongoDatabase database,
        string? collectionName = null)
    {
        var name = collectionName ?? $"agent_states_{typeof(TState).Name}";
        _collection = database.GetCollection<AgentStateDocument>(name);
        _stateTypeName = typeof(TState).FullName ?? typeof(TState).Name;

        // Ensure indexes are created (idempotent)
        MongoDBIndexManager.EnsureStateStoreIndexes(_collection);
    }

    /// <summary>
    /// Load state from MongoDB and deserialize from Protobuf bytes
    /// </summary>
    public async Task<TState?> LoadAsync(Guid agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
                                   .FirstOrDefaultAsync(ct)
                                   .ConfigureAwait(false);

        if (doc == null || doc.StateData == null || doc.StateData.Length == 0)
            return null;

        // Deserialize from Protobuf bytes
        var state = new TState();
        state.MergeFrom(doc.StateData);
        return state;
    }

    /// <summary>
    /// Serialize state to Protobuf bytes and save to MongoDB (upsert)
    /// </summary>
    public async Task SaveAsync(Guid agentId, TState state, CancellationToken ct = default)
    {
        var doc = new AgentStateDocument
        {
            AgentId = agentId,
            StateData = SerializeState(state),
            StateType = _stateTypeName,
            Version = 1,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Save with version control (for EventSourcing snapshots)
    /// Version represents the event version at snapshot time
    /// </summary>
    public async Task SaveAsync(Guid agentId, TState state, long version, CancellationToken ct = default)
    {
        var doc = new AgentStateDocument
        {
            AgentId = agentId,
            StateData = SerializeState(state),
            StateType = _stateTypeName,
            Version = version,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Get current version (for EventSourcing replay optimization)
    /// </summary>
    public async Task<long> GetCurrentVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
                                   .Project(x => x.Version)
                                   .FirstOrDefaultAsync(ct)
                                   .ConfigureAwait(false);
        return doc;
    }

    /// <summary>
    /// Delete state from MongoDB
    /// </summary>
    public async Task DeleteAsync(Guid agentId, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.AgentId == agentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if state exists
    /// </summary>
    public async Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default)
    {
        var count = await _collection.CountDocumentsAsync(x => x.AgentId == agentId, cancellationToken: ct)
                                      .ConfigureAwait(false);
        return count > 0;
    }

    /// <summary>
    /// Serialize Protobuf state to byte array
    /// </summary>
    private static byte[] SerializeState(TState state)
    {
        return state.ToByteArray();
    }
}

/// <summary>
/// MongoDB state store factory for DI
/// </summary>
public static class MongoDBStateStoreFactory
{
    /// <summary>
    /// Create MongoDB state store factory function using DI-registered IMongoDatabase
    /// </summary>
    /// <typeparam name="TState">State type (must be Protobuf IMessage)</typeparam>
    /// <param name="collectionName">Optional custom collection name</param>
    /// <returns>Factory function for DI</returns>
    public static Func<IServiceProvider, object> Create<TState>(string? collectionName = null)
        where TState : class, IMessage<TState>, new()
    {
        return sp =>
        {
            var database = sp.GetService(typeof(IMongoDatabase)) as IMongoDatabase
                ?? throw new InvalidOperationException(
                    "IMongoDatabase not registered. Call services.AddAevatarMongoDB() first.");
            return new MongoDBStateStore<TState>(database, collectionName);
        };
    }
}
