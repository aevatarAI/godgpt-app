using System;
using System.Threading.Tasks;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Service interface for platform price synchronization.
/// </summary>
public interface IPlatformPriceSyncService
{
    /// <summary>
    /// Syncs all platform prices to local storage.
    /// </summary>
    Task SyncAllPricesAsync();
    
    /// <summary>
    /// Syncs prices for a specific subscription product.
    /// </summary>
    Task SyncProductPricesAsync(string subscriptionProductId);
    
    /// <summary>
    /// Gets the last sync timestamp.
    /// </summary>
    Task<DateTime> GetLastSyncTimeAsync();
}
