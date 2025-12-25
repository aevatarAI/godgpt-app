using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB document for AI memory messages (append-only).
///
/// Design:
/// - One shared collection across agents (partitioned by AgentId)
/// - Optional SessionId to support per-session scoping
/// - Query patterns:
///   - GetHistory: AgentId (+ optional SessionId), sort by CreatedAt
///   - Search: AgentId (+ optional SessionId), full-text search on Content
/// </summary>
internal sealed class AIMemoryMessageDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRequired]
    public string AgentId { get; set; } = string.Empty;

    public string? SessionId { get; set; }

    [BsonRequired]
    public string Role { get; set; } = string.Empty;

    [BsonRequired]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

