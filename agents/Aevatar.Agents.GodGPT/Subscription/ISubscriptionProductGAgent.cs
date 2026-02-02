using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Orleans.Concurrency;

using PaymentPlatform = Aevatar.Agents.GodGPT.Protos.InviteCode.PaymentPlatform;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing subscription products.
/// RPC methods return Protobuf wrapper types because RpcProxy only supports IMessage.
/// </summary>
public interface ISubscriptionProductGAgent : IGAgent
{
    // CRUD
    Task<SubscriptionProduct> CreateProductAsync(CreateProductDto dto);
    Task<SubscriptionProduct> UpdateProductAsync(string productId, UpdateProductDto dto);
    Task DeleteProductAsync(string productId);
    Task<SubscriptionProduct> SetProductListedAsync(string productId, bool isListed);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    // Returns Protobuf wrapper types for RPC compatibility
    [AlwaysInterleave]
    Task<SubscriptionProduct?> GetProductAsync(string productId);
    
    [AlwaysInterleave]
    Task<SubscriptionProductList> GetAllProductsAsync();
    
    [AlwaysInterleave]
    Task<SubscriptionProductList> GetListedProductsByPlatformAsync(PaymentPlatform platform);
    
    [AlwaysInterleave]
    Task<SubscriptionProduct?> GetProductByPlatformProductIdAsync(string platformProductId, PaymentPlatform platform);
}
