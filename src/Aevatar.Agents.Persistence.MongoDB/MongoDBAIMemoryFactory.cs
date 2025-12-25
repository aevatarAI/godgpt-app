using System;
using Aevatar.Agents.AI.Abstractions;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB-backed <see cref="IAevatarAIMemoryFactory"/>.
/// Creates per-agent <see cref="MongoDBAIMemory"/> instances.
/// </summary>
public sealed class MongoDBAIMemoryFactory : IAevatarAIMemoryFactory
{
    private readonly IMongoDatabase _database;
    private readonly string? _collectionName;

    public MongoDBAIMemoryFactory(IMongoDatabase database, string? collectionName = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _collectionName = string.IsNullOrWhiteSpace(collectionName) ? null : collectionName.Trim();
    }

    public IAevatarAIMemory Create(string agentId, string? sessionId = null)
    {
        return new MongoDBAIMemory(_database, agentId, sessionId, _collectionName);
    }
}

