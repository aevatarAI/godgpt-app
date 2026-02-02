using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing subscription features.
/// RPC methods return Protobuf wrapper types because RpcProxy only supports IMessage.
/// </summary>
public interface ISubscriptionFeatureGAgent : IGAgent
{
    // CRUD
    Task<SubscriptionFeature> CreateFeatureAsync(CreateSubscriptionFeatureDto dto);
    Task<SubscriptionFeature> UpdateFeatureAsync(string featureId, UpdateSubscriptionFeatureDto dto);
    Task DeleteFeatureAsync(string featureId);
    Task ReorderFeaturesAsync(SubscriptionFeatureOrderListDto orders);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    // Returns Protobuf wrapper types for RPC compatibility
    [AlwaysInterleave]
    Task<SubscriptionFeature?> GetFeatureAsync(string featureId);
    
    [AlwaysInterleave]
    Task<SubscriptionFeatureList> GetAllFeaturesAsync();
    
    [AlwaysInterleave]
    Task<SubscriptionFeatureList> GetFeaturesByIdsAsync(SubscriptionFeatureIdList featureIds);
    
    [AlwaysInterleave]
    Task<SubscriptionFeatureList> GetFeaturesByTypeAsync(SubscriptionFeatureType type);
    
    [AlwaysInterleave]
    Task<SubscriptionFeatureList> GetFeaturesByUsageAsync(SubscriptionFeatureUsage usage);
}
