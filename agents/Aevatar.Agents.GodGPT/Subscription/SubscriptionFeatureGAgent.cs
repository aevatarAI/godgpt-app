using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for managing subscription features with event sourcing.
/// </summary>
public class SubscriptionFeatureGAgent : 
    GAgentBase<SubscriptionFeatureState>,
    ISubscriptionFeatureGAgent
{
    private readonly ILogger<SubscriptionFeatureGAgent> _logger;

    public SubscriptionFeatureGAgent(ILogger<SubscriptionFeatureGAgent> logger)
    {
        _logger = logger;
    }

    #region CRUD Operations

    public async Task<SubscriptionFeature> CreateFeatureAsync(CreateSubscriptionFeatureDto dto)
    {
        var featureId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("Creating subscription feature: {FeatureId}, NameKey: {NameKey}, Type: {Type}", 
            featureId, dto.NameKey, dto.Type);

        RaiseEvent(new SubscriptionFeatureCreatedEvent
        {
            FeatureId = featureId,
            NameKey = dto.NameKey,
            DescriptionKey = dto.DescriptionKey,
            Type = dto.Type,
            DisplayOrder = dto.DisplayOrder,
            Usage = dto.Usage
        });
        
        await ConfirmEventsAsync();
        
        return State.Features[featureId];
    }

    public async Task<SubscriptionFeature> UpdateFeatureAsync(
        string featureId, UpdateSubscriptionFeatureDto dto)
    {
        if (!State.Features.ContainsKey(featureId))
            throw new KeyNotFoundException($"Feature not found: {featureId}");
        
        _logger.LogInformation("Updating subscription feature: {FeatureId}", featureId);

        var updatedEvent = new SubscriptionFeatureUpdatedEvent
        {
            FeatureId = featureId
        };
        
        if (dto.HasNameKey) updatedEvent.NameKey = dto.NameKey;
        if (dto.HasDescriptionKey) updatedEvent.DescriptionKey = dto.DescriptionKey;
        if (dto.HasType) updatedEvent.Type = dto.Type;
        if (dto.HasDisplayOrder) updatedEvent.DisplayOrder = dto.DisplayOrder;
        if (dto.HasUsage) updatedEvent.Usage = dto.Usage;
        
        RaiseEvent(updatedEvent);
        await ConfirmEventsAsync();
        
        return State.Features[featureId];
    }

    public async Task DeleteFeatureAsync(string featureId)
    {
        if (!State.Features.ContainsKey(featureId))
            throw new KeyNotFoundException($"Feature not found: {featureId}");
        
        _logger.LogInformation("Deleting subscription feature: {FeatureId}", featureId);

        RaiseEvent(new SubscriptionFeatureDeletedEvent { FeatureId = featureId });
        
        await ConfirmEventsAsync();
    }

    public async Task ReorderFeaturesAsync(SubscriptionFeatureOrderListDto orders)
    {
        _logger.LogInformation("Reordering {Count} subscription features", orders.Orders.Count);

        var reorderedEvent = new SubscriptionFeaturesReorderedEvent();
        reorderedEvent.Orders.AddRange(orders.Orders.Select(o => new SubscriptionFeatureOrderItem
        {
            FeatureId = o.FeatureId,
            DisplayOrder = o.DisplayOrder
        }));
        RaiseEvent(reorderedEvent);
        await ConfirmEventsAsync();
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<SubscriptionFeature?> GetFeatureAsync(string featureId)
    {
        if (State.Features.TryGetValue(featureId, out var feature))
            return Task.FromResult<SubscriptionFeature?>(feature);
        return Task.FromResult<SubscriptionFeature?>(null);
    }

    public Task<SubscriptionFeatureList> GetAllFeaturesAsync()
    {
        var subscriptionFeatureList = new SubscriptionFeatureList();
        var features = State.Features.Values
            .OrderBy(f => f.DisplayOrder);
        subscriptionFeatureList.Features.AddRange(features);
        return Task.FromResult(subscriptionFeatureList);
    }

    public Task<SubscriptionFeatureList> GetFeaturesByIdsAsync(SubscriptionFeatureIdList request)
    {
        var subscriptionFeatureList = new SubscriptionFeatureList();
        var features = State.Features.Values
            .Where(f => request.FeatureIds.Contains(f.Id))
            .OrderBy(f => f.DisplayOrder);
        subscriptionFeatureList.Features.AddRange(features);
        return Task.FromResult(subscriptionFeatureList);
    }

    public Task<SubscriptionFeatureList> GetFeaturesByTypeAsync(SubscriptionFeatureType type)
    {
        var subscriptionFeatureList = new SubscriptionFeatureList();
        var features = State.Features.Values
            .Where(f => f.Type == type)
            .OrderBy(f => f.DisplayOrder);
        subscriptionFeatureList.Features.AddRange(features);
        return Task.FromResult(subscriptionFeatureList);
    }

    public Task<SubscriptionFeatureList> GetFeaturesByUsageAsync(SubscriptionFeatureUsage usage)
    {
        var subscriptionFeatureList = new SubscriptionFeatureList();
        var features = State.Features.Values
            .Where(f => f.Usage == usage)
            .OrderBy(f => f.DisplayOrder);
        subscriptionFeatureList.Features.AddRange(features);
        return Task.FromResult(subscriptionFeatureList);
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages subscription features.");
    }

    #endregion

    #region State Transition

    protected override void TransitionState(SubscriptionFeatureState state, IMessage evt)
    {
        switch (evt)
        {
            case SubscriptionFeatureCreatedEvent created:
                state.Features[created.FeatureId] = new SubscriptionFeature
                {
                    Id = created.FeatureId,
                    NameKey = created.NameKey,
                    DescriptionKey = created.DescriptionKey,
                    Type = created.Type,
                    DisplayOrder = created.DisplayOrder,
                    Usage = created.Usage,
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                break;
                
            case SubscriptionFeatureUpdatedEvent updated:
                if (state.Features.TryGetValue(updated.FeatureId, out var feature))
                {
                    if (updated.HasNameKey) feature.NameKey = updated.NameKey;
                    if (updated.HasDescriptionKey) feature.DescriptionKey = updated.DescriptionKey;
                    if (updated.HasType) feature.Type = updated.Type;
                    if (updated.HasDisplayOrder) feature.DisplayOrder = updated.DisplayOrder;
                    if (updated.HasUsage) feature.Usage = updated.Usage;
                    feature.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                }
                break;
                
            case SubscriptionFeatureDeletedEvent deleted:
                state.Features.Remove(deleted.FeatureId);
                break;
                
            case SubscriptionFeaturesReorderedEvent reordered:
                foreach (var order in reordered.Orders)
                {
                    if (state.Features.TryGetValue(order.FeatureId, out var f))
                    {
                        f.DisplayOrder = order.DisplayOrder;
                        f.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                    }
                }
                break;
        }
    }

    #endregion
}
