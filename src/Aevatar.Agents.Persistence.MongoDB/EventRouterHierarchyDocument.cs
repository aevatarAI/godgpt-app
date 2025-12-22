using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB document for EventRouter hierarchy
/// </summary>
public class EventRouterHierarchyDocument
{
    /// <summary>
    /// Agent ID (primary key)
    /// </summary>
    [BsonId]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Parent agent ID (null if root)
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Collection of child agent IDs
    /// </summary>
    public List<string> ChildrenIds { get; set; } = new();

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
