using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.Core.Internal;

/// <summary>
/// Internal contract that exposes hierarchy mutation operations for Actor implementations.
/// SDK users cannot access this interface; only runtime managers and tests can access it via InternalsVisibleTo.
/// </summary>
internal interface IActorHierarchyOperations
{
    Task AddChildAsync(string childId, CancellationToken ct = default);

    Task RemoveChildAsync(string childId, CancellationToken ct = default);

    Task SetParentAsync(string parentId, CancellationToken ct = default);

    Task ClearParentAsync(CancellationToken ct = default);
}

