using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

// Alias for clarity
using PaymentPlatform = Aevatar.Agents.GodGPT.Protos.InviteCode.PaymentPlatform;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for managing subscription products with event sourcing.
/// </summary>
public class SubscriptionProductGAgent : 
    GAgentBase<SubscriptionProductState>,
    ISubscriptionProductGAgent
{
    private readonly ILogger<SubscriptionProductGAgent> _logger;

    public SubscriptionProductGAgent(ILogger<SubscriptionProductGAgent> logger)
    {
        _logger = logger;
    }

    #region CRUD Operations

    public async Task<SubscriptionProduct> CreateProductAsync(CreateProductDto dto)
    {
        var productId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("Creating subscription product: {ProductId}, Platform: {Platform}", 
            productId, dto.Platform);

        var evt = new ProductCreatedEvent
        {
            ProductId = productId,
            NameKey = dto.NameKey,
            PlanType = dto.PlanType,
            DescriptionKey = dto.DescriptionKey,
            HighlightKey = dto.HighlightKey,
            IsUltimate = dto.IsUltimate,
            PlatformProductId = dto.PlatformProductId,
            Platform = dto.Platform,
            DisplayOrder = dto.DisplayOrder
        };
        
        if (dto.HasLabelId)
            evt.LabelId = dto.LabelId;
        
        foreach (var featureId in dto.FeatureIds)
            evt.FeatureIds.Add(featureId);

        RaiseEvent(evt);
        await ConfirmEventsAsync();
        
        return State.Products[productId];
    }

    public async Task<SubscriptionProduct> UpdateProductAsync(string productId, UpdateProductDto dto)
    {
        if (!State.Products.ContainsKey(productId))
            throw new KeyNotFoundException($"Product not found: {productId}");
        
        _logger.LogInformation("Updating subscription product: {ProductId}", productId);

        var evt = new ProductUpdatedEvent
        {
            ProductId = productId
        };
        if (dto.HasNameKey)
            evt.NameKey = dto.NameKey;
        if (dto.HasDescriptionKey)
            evt.DescriptionKey = dto.DescriptionKey;
        if (dto.HasHighlightKey)
            evt.HighlightKey = dto.HighlightKey;
        if (dto.HasIsUltimate)
            evt.IsUltimate = dto.IsUltimate;
        if (dto.HasLabelId)
            evt.LabelId = dto.LabelId;
        if (dto.HasPlanType)
            evt.PlanType = dto.PlanType;
        if (dto.HasDisplayOrder)
            evt.DisplayOrder = dto.DisplayOrder;
        if (dto.FeatureIds != null)
        {
            foreach (var featureId in dto.FeatureIds)
                evt.FeatureIds.Add(featureId);
        }

        RaiseEvent(evt);
        await ConfirmEventsAsync();
        
        return State.Products[productId];
    }

    public async Task DeleteProductAsync(string productId)
    {
        if (!State.Products.ContainsKey(productId))
            throw new KeyNotFoundException($"Product not found: {productId}");
        
        _logger.LogInformation("Deleting subscription product: {ProductId}", productId);

        RaiseEvent(new ProductDeletedEvent { ProductId = productId });
        
        await ConfirmEventsAsync();
    }

    public async Task<SubscriptionProduct> SetProductListedAsync(string productId, bool isListed)
    {
        if (!State.Products.ContainsKey(productId))
            throw new KeyNotFoundException($"Product not found: {productId}");
        
        _logger.LogInformation("Setting product {ProductId} listed status to: {IsListed}", 
            productId, isListed);

        RaiseEvent(new ProductListedEvent
        {
            ProductId = productId,
            IsListed = isListed
        });
        
        await ConfirmEventsAsync();
        
        return State.Products[productId];
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<SubscriptionProduct?> GetProductAsync(string productId)
    {
        if (State.Products.TryGetValue(productId, out var product))
            return Task.FromResult<SubscriptionProduct?>(product);
        return Task.FromResult<SubscriptionProduct?>(null);
    }

    public Task<SubscriptionProductList> GetAllProductsAsync()
    {
        var subscriptionProductList = new SubscriptionProductList();
        subscriptionProductList.Products.AddRange(State.Products.Values);
        return Task.FromResult(subscriptionProductList);
    }

    public Task<SubscriptionProductList> GetListedProductsByPlatformAsync(PaymentPlatform platform)
    {
        var products = State.Products.Values
            .Where(p => p.Platform == platform && p.HasIsListed && p.IsListed);
        var subscriptionProductList = new SubscriptionProductList();
        subscriptionProductList.Products.AddRange(products);
        return Task.FromResult(subscriptionProductList);
    }

    public Task<SubscriptionProduct?> GetProductByPlatformProductIdAsync(
        string platformProductId, PaymentPlatform platform)
    {
        var product = State.Products.Values
            .FirstOrDefault(p => p.PlatformProductId == platformProductId && p.Platform == platform);
        return Task.FromResult(product);
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages subscription products.");
    }

    #endregion

    #region State Transition

    protected override void TransitionState(SubscriptionProductState state, IMessage evt)
    {
        switch (evt)
        {
            case ProductCreatedEvent created:
                var newProduct = new SubscriptionProduct
                {
                    Id = created.ProductId,
                    NameKey = created.NameKey,
                    PlanType = created.PlanType,
                    DescriptionKey = created.DescriptionKey,
                    HighlightKey = created.HighlightKey,
                    IsUltimate = created.IsUltimate,
                    PlatformProductId = created.PlatformProductId,
                    Platform = created.Platform,
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                    DisplayOrder = created.DisplayOrder
                };
                if (created.HasLabelId)
                    newProduct.LabelId = created.LabelId;
                foreach (var featureId in created.FeatureIds)
                    newProduct.FeatureIds.Add(featureId);
                state.Products[created.ProductId] = newProduct;
                break;
                
            case ProductUpdatedEvent updated:
                if (state.Products.TryGetValue(updated.ProductId, out var product))
                {
                    if (updated.HasNameKey) product.NameKey = updated.NameKey;
                    if (updated.HasLabelId)
                    {
                        if (updated.LabelId == string.Empty)
                            product.ClearLabelId();
                        else
                            product.LabelId = updated.LabelId;
                    }
                    if (updated.HasPlanType) product.PlanType = updated.PlanType;
                    if (updated.HasDescriptionKey) product.DescriptionKey = updated.DescriptionKey;
                    if (updated.HasHighlightKey) product.HighlightKey = updated.HighlightKey;
                    if (updated.HasIsUltimate) product.IsUltimate = updated.IsUltimate;
                    if (updated.FeatureIds.Count > 0)
                    {
                        product.FeatureIds.Clear();
                        foreach (var featureId in updated.FeatureIds)
                            product.FeatureIds.Add(featureId);
                    }
                    if (updated.HasDisplayOrder) product.DisplayOrder = updated.DisplayOrder;
                    product.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                }
                break;
                
            case ProductDeletedEvent deleted:
                state.Products.Remove(deleted.ProductId);
                break;
                
            case ProductListedEvent listed:
                if (state.Products.TryGetValue(listed.ProductId, out var p))
                {
                    p.IsListed = listed.IsListed;
                    p.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                }
                break;
        }
    }

    #endregion
}
