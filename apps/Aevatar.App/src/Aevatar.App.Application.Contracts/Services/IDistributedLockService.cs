using System;
using System.Threading.Tasks;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// Distributed lock service interface
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Try to acquire a distributed lock
    /// </summary>
    /// <param name="lockKey">Lock key</param>
    /// <param name="expiration">Lock expiration time</param>
    /// <param name="lockValue">Lock value (usually a unique identifier)</param>
    /// <returns>True if lock acquired successfully, false otherwise</returns>
    Task<bool> TryAcquireLockAsync(string lockKey, TimeSpan expiration, string lockValue);

    /// <summary>
    /// Release a distributed lock
    /// </summary>
    /// <param name="lockKey">Lock key</param>
    /// <param name="lockValue">Lock value (must match the value used when acquiring the lock)</param>
    /// <returns>True if lock released successfully, false otherwise</returns>
    Task<bool> ReleaseLockAsync(string lockKey, string lockValue);

    /// <summary>
    /// Execute action with distributed lock
    /// </summary>
    /// <param name="lockKey">Lock key</param>
    /// <param name="expiration">Lock expiration time</param>
    /// <param name="action">Action to execute</param>
    /// <returns>True if action executed successfully with lock, false if failed to acquire lock</returns>
    Task<bool> ExecuteWithLockAsync(string lockKey, TimeSpan expiration, Func<Task> action);
}
