namespace Aevatar.Agents.Abstractions.Helpers;

/// <summary>
/// AgentId normalization utility.
///
/// Core convention (strong constraint):
/// - **System-wide unique identifier** uses full format: <c>"AgentTypeShortName:RawId"</c>
/// - RawId (usually Guid string) can be passed during creation, system will automatically assemble into full ActorId
/// - All subsequent Manager/Stream/Hierarchy operations should use full ActorId (i.e., <see cref="IGAgent.Id"/> / <see cref="IGAgentActor.Id"/>)
///
/// WHY:
/// - Orleans GrainKey/StreamKey must contain type information, otherwise same RawId with different types will conflict
/// - After unified format, ID behavior is consistent across Local/Proto/Orleans three runtimes
/// </summary>
public static class AgentId
{
    public const char Separator = ':';

    /// <summary>
    /// Get AgentType short name from <see cref="Type"/> (without namespace, without generic arity).
    /// </summary>
    public static string GetAgentTypeShortName(Type agentType)
    {
        ArgumentNullException.ThrowIfNull(agentType);

        var name = agentType.Name;
        var tick = name.IndexOf('`');
        return tick > 0 ? name[..tick] : name;
    }

    /// <summary>
    /// Extract AgentType short name from assembly-qualified name/full name.
    ///
    /// Examples:
    /// - "Aevatar.App.Agents.UserQuotaGAgent, Aevatar.App.Agents" -> "UserQuotaGAgent"
    /// - "Aevatar.App.Agents.UserQuotaGAgent" -> "UserQuotaGAgent"
    /// </summary>
    public static string GetAgentTypeShortName(string agentTypeName)
    {
        if (string.IsNullOrWhiteSpace(agentTypeName))
            return string.Empty;

        var fullName = agentTypeName.Split(',')[0].Trim();
        var lastDot = fullName.LastIndexOf('.');
        var shortName = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;

        var tick = shortName.IndexOf('`');
        return tick > 0 ? shortName[..tick] : shortName;
    }

    /// <summary>
    /// Normalize user input id (RawId or ActorId) to ActorId ("Type:RawId").
    ///
    /// Rules:
    /// - If input is already "Type:RawId" and Type matches, return as-is
    /// - If input contains no separator, treat as RawId, automatically prepend prefix
    /// - If input contains separator but Type doesn't match, throw exception directly (avoid cross-type misuse/double concatenation)
    /// </summary>
    public static string Normalize(Type agentType, string id)
    {
        ArgumentNullException.ThrowIfNull(agentType);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("id cannot be null/empty.", nameof(id));
        }

        id = id.Trim();

        var typeShortName = GetAgentTypeShortName(agentType);
        var sep = id.IndexOf(Separator);
        if (sep < 0)
        {
            // RawId -> ActorId
            return $"{typeShortName}{Separator}{id}";
        }

        // Already looks like ActorId. Validate type prefix.
        var prefix = id[..sep];
        var rawId = sep < id.Length - 1 ? id[(sep + 1)..] : string.Empty;
        if (string.IsNullOrWhiteSpace(rawId))
        {
            throw new ArgumentException($"Invalid actor id '{id}': raw id part is empty.", nameof(id));
        }

        if (!string.Equals(prefix, typeShortName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid actor id '{id}': type prefix '{prefix}' does not match '{typeShortName}'. " +
                "Pass a raw id (e.g., Guid string) or a correctly prefixed actor id.",
                nameof(id));
        }

        return id;
    }

    public static string Normalize<TAgent>(string id) where TAgent : IGAgent
        => Normalize(typeof(TAgent), id);

    /// <summary>
    /// Try to split ActorId -> (TypeShortName, RawId).
    /// </summary>
    public static bool TrySplit(string actorId, out string agentTypeShortName, out string rawId)
    {
        agentTypeShortName = string.Empty;
        rawId = string.Empty;

        if (string.IsNullOrWhiteSpace(actorId))
            return false;

        var s = actorId.Trim();
        var idx = s.IndexOf(Separator);
        if (idx <= 0 || idx >= s.Length - 1)
            return false;

        agentTypeShortName = s[..idx];
        rawId = s[(idx + 1)..];
        return !string.IsNullOrWhiteSpace(agentTypeShortName) && !string.IsNullOrWhiteSpace(rawId);
    }

    /// <summary>
    /// Extract RawId from ActorId. If input is not ActorId, return as RawId (trimmed).
    /// </summary>
    public static string ExtractRawId(string idOrActorId)
    {
        if (TrySplit(idOrActorId, out _, out var raw))
            return raw;

        return string.IsNullOrWhiteSpace(idOrActorId) ? string.Empty : idOrActorId.Trim();
    }
}


