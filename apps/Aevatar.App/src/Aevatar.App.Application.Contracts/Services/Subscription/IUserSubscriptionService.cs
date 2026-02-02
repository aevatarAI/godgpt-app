using System;
using System.Threading.Tasks;
using Aevatar.App.Services.Subscription.Dtos;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service interface for user subscription queries.
/// </summary>
public interface IUserSubscriptionService
{
    /// <summary>
    /// Gets the current subscription information for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>User subscription information, always returns an object (never null).</returns>
    Task<UserSubscriptionDto> GetCurrentSubscriptionAsync(Guid userId);
}
