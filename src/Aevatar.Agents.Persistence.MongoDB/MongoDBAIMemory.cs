using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf.WellKnownTypes;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB-backed implementation of <see cref="IAevatarAIMemory"/>.
///
/// Scope:
/// - One instance is bound to ONE agent id (and optionally a session id).
/// - This keeps the <see cref="IAevatarAIMemory"/> interface minimal while still enabling isolation.
///
/// Storage:
/// - Collection: "ai_memory_messages" (default)
/// - Document: <see cref="AIMemoryMessageDocument"/>
/// </summary>
public sealed class MongoDBAIMemory : IAevatarAIMemory
{
    private readonly IMongoCollection<AIMemoryMessageDocument> _collection;
    private readonly string _agentId;
    private readonly string? _sessionId;

    // Static constructor ensures BSON serializers are configured once per process
    static MongoDBAIMemory()
    {
        MongoDBServiceCollectionExtensions.ConfigureBsonSerializers();
    }

    public MongoDBAIMemory(
        IMongoDatabase database,
        string agentId,
        string? sessionId = null,
        string? collectionName = null)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        _agentId = agentId.Trim();
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();

        var name = string.IsNullOrWhiteSpace(collectionName) ? "ai_memory_messages" : collectionName.Trim();
        _collection = database.GetCollection<AIMemoryMessageDocument>(name);

        // Ensure indexes are created (idempotent)
        MongoDBIndexManager.EnsureAIMemoryIndexes(_collection);
    }

    public async Task AddMessageAsync(
        string role,
        string content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(content))
            return;

        var doc = new AIMemoryMessageDocument
        {
            AgentId = _agentId,
            SessionId = _sessionId,
            Role = string.IsNullOrWhiteSpace(role) ? "unknown" : role.Trim(),
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        await _collection.InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AevatarConversationEntry>> GetHistoryAsync(
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveLimit = limit is > 0 ? limit.Value : 200;
        effectiveLimit = Math.Clamp(effectiveLimit, 1, 2000);

        var filter = Builders<AIMemoryMessageDocument>.Filter.Eq(x => x.AgentId, _agentId);
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            filter &= Builders<AIMemoryMessageDocument>.Filter.Eq(x => x.SessionId, _sessionId);
        }

        // Fetch latest N then reverse to chronological order
        var docs = await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(effectiveLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        docs.Reverse();

        var result = new List<AevatarConversationEntry>(docs.Count);
        foreach (var d in docs)
        {
            result.Add(new AevatarConversationEntry
            {
                Role = d.Role ?? string.Empty,
                Content = d.Content ?? string.Empty,
                Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc))
            });
        }

        return result;
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<AIMemoryMessageDocument>.Filter.Eq(x => x.AgentId, _agentId);
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            filter &= Builders<AIMemoryMessageDocument>.Filter.Eq(x => x.SessionId, _sessionId);
        }

        await _collection.DeleteManyAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();

        var k = Math.Clamp(topK, 1, 50);

        var baseFilter = Builders<AIMemoryMessageDocument>.Filter.Eq(x => x.AgentId, _agentId);
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            baseFilter &= Builders<AIMemoryMessageDocument>.Filter.Eq(x => x.SessionId, _sessionId);
        }

        // Prefer $text if index exists; fallback to regex if not.
        try
        {
            var textFilter = Builders<AIMemoryMessageDocument>.Filter.Text(query);
            var filter = baseFilter & textFilter;

            var docs = await _collection.Find(filter)
                .Sort(Builders<AIMemoryMessageDocument>.Sort.MetaTextScore("score"))
                .Limit(k)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return docs.Select(d => $"[{d.Role}] {d.Content}".Trim()).ToList();
        }
        catch (MongoCommandException)
        {
            // text index missing or not allowed; fall back to regex (best-effort).
        }

        var regex = new BsonRegularExpression(query, "i");
        var regexFilter = Builders<AIMemoryMessageDocument>.Filter.Regex(x => x.Content, regex);
        var finalFilter = baseFilter & regexFilter;

        var fallbackDocs = await _collection.Find(finalFilter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(k)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return fallbackDocs.Select(d => $"[{d.Role}] {d.Content}".Trim()).ToList();
    }
}

