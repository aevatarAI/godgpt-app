using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.Core.Internal;

/// <summary>
/// Internal contract that exposes hierarchy mutation operations for Actor implementations.
/// SDK 使用者无法访问该接口，只有运行时管理器和测试通过 InternalsVisibleTo 访问。
/// </summary>
internal interface IActorHierarchyOperations
{
    Task AddChildAsync(string childId, CancellationToken ct = default);

    Task RemoveChildAsync(string childId, CancellationToken ct = default);

    Task SetParentAsync(string parentId, CancellationToken ct = default);

    Task ClearParentAsync(CancellationToken ct = default);
}

