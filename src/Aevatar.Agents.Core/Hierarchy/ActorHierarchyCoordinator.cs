using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Internal;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Hierarchy;

/// <summary>
/// Public utility class: Ensures parent-child relationship operations complete atomically,
/// internally touches both parties' EventRouter simultaneously.
/// </summary>
public static class ActorHierarchyCoordinator
{
    public static async Task LinkAsync(
        IGAgentActor parent,
        IGAgentActor child,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (parent == null)
        {
            throw new ArgumentNullException(nameof(parent));
        }

        if (child == null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        if (parent.Id == child.Id)
        {
            throw new InvalidOperationException("Parent and child cannot be the same actor.");
        }

        var parentOps = GetHierarchyOps(parent);
        var childOps = GetHierarchyOps(child);

        await parentOps.AddChildAsync(child.Id, ct);
        try
        {
            await childOps.SetParentAsync(parent.Id, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "Failed to set parent {ParentId} for child {ChildId}, rolling back parent registration",
                parent.Id, child.Id);

            try
            {
                await parentOps.RemoveChildAsync(child.Id, CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                logger?.LogError(rollbackEx,
                    "Rollback failed while removing child {ChildId} from parent {ParentId}",
                    child.Id, parent.Id);
            }

            throw;
        }
    }

    public static async Task UnlinkAsync(
        IGAgentActor child,
        IGAgentActor? parent = null,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (child == null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        var childOps = GetHierarchyOps(child);

        if (parent == null)
        {
            await childOps.ClearParentAsync(ct);
            return;
        }

        var parentOps = GetHierarchyOps(parent);

        await childOps.ClearParentAsync(ct);
        try
        {
            await parentOps.RemoveChildAsync(child.Id, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "Failed to remove child {ChildId} from parent {ParentId}, attempting to restore",
                child.Id, parent.Id);

            try
            {
                await childOps.SetParentAsync(parent.Id, CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                logger?.LogError(rollbackEx,
                    "Failed to restore parent {ParentId} on child {ChildId} after unlink failure",
                    parent.Id, child.Id);
            }

            throw;
        }
    }

    private static IActorHierarchyOperations GetHierarchyOps(IGAgentActor actor)
    {
        if (actor is IActorHierarchyOperations ops)
        {
            return ops;
        }

        throw new InvalidOperationException(
            $"Actor type '{actor.GetType().FullName}' does not expose hierarchy operations.");
    }
}

