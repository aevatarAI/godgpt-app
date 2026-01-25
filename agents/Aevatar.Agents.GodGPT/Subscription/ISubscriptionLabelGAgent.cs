using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing subscription labels.
/// RPC methods return Protobuf wrapper types because RpcProxy only supports IMessage.
/// </summary>
public interface ISubscriptionLabelGAgent : IGAgent
{
    // CRUD
    Task<SubscriptionLabel> CreateLabelAsync(CreateSubscriptionLabelDto dto);
    Task<SubscriptionLabel> UpdateLabelAsync(string labelId, UpdateSubscriptionLabelDto dto);
    Task DeleteLabelAsync(string labelId);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    // Returns Protobuf wrapper types for RPC compatibility
    [AlwaysInterleave]
    Task<SubscriptionLabel?> GetLabelAsync(string labelId);
    
    [AlwaysInterleave]
    Task<SubscriptionLabelList> GetAllLabelsAsync();
}
