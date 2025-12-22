namespace Aevatar.Agents.Abstractions.EventRouting;

/// <summary>
/// Data model for persisting EventRouter hierarchy relationships
/// </summary>
public class EventRouterHierarchy
{
    /// <summary>
    /// Parent agent ID (null if this is a root agent)
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Collection of child agent IDs
    /// </summary>
    public HashSet<string> ChildrenIds { get; set; } = new();
}
