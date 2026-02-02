using Aevatar.Agents.GodGPT.Protos.Subscription;
using Orleans.Concurrency;
using PaymentPlatform = Aevatar.Agents.GodGPT.Protos.InviteCode.PaymentPlatform;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing platform prices (data storage layer).
/// RPC methods return Protobuf wrapper types because RpcProxy only supports IMessage.
/// </summary>
public interface IPlatformPriceGAgent : IGAgent
{
    // Sync prices
    Task<int> SyncProductPricesFromPlatformAsync(
        string productId,
        PaymentPlatform platform,
        PlatformPriceInfoList platformPriceList);
    
    Task MarkSyncCompletedAsync();
    
    // General price management (admin manual configuration for Apple/Google prices)
    Task<PlatformPrice> SetPriceAsync(string productId, SetPriceDto dto);
    Task DeletePriceAsync(string productId, PaymentPlatform platform, string currency);
    
    /// <summary>
    /// Delete price by platform-specific price ID (e.g., Stripe price_xxx).
    /// </summary>
    Task DeletePriceByPlatformIdAsync(string platformPriceId);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    // Returns Protobuf wrapper types for RPC compatibility
    [AlwaysInterleave]
    Task<PlatformPriceList> GetPricesByProductIdAsync(string productId);
    
    [AlwaysInterleave]
    Task<PlatformPrice?> GetPriceByPlatformPriceIdAsync(string platformPriceId);
    
    [AlwaysInterleave]
    Task<DateTime> GetLastPlatformPriceSyncTimeAsync();
}
