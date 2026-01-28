using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

using PaymentPlatform = Aevatar.Agents.GodGPT.Protos.InviteCode.PaymentPlatform;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for managing platform prices with event sourcing (data storage layer).
/// Handles price data storage and price synchronization.
/// </summary>
public class PlatformPriceGAgent : 
    GAgentBase<PlatformPriceState>,
    IPlatformPriceGAgent
{
    private readonly ILogger<PlatformPriceGAgent> _logger;
    private readonly IGAgentActorFactory _actorFactory;

    public PlatformPriceGAgent(
        ILogger<PlatformPriceGAgent> logger,
        IGAgentActorFactory actorFactory)
    {
        _logger = logger;
        _actorFactory = actorFactory;
    }

    #region Platform Price Sync Operations

    /// <summary>
    /// Syncs prices for a single product: updates/adds from platform, deletes those not on platform.
    /// </summary>
    /// <returns>Number of price operations performed.</returns>
    public async Task<int> SyncProductPricesFromPlatformAsync(
        string productId,
        PaymentPlatform platform,
        PlatformPriceInfoList platformPriceList)
    {
        var operationCount = 0;

        State.ProductPrices.TryGetValue(productId, out var localPriceList);
        var localPlatformPrices = (localPriceList?.Prices ?? Enumerable.Empty<PlatformPrice>())
            .Where(p => p.Platform == platform)
            .ToList();
        
        var platformPriceIds = platformPriceList.Prices.Select(p => p.PriceId).ToHashSet();

        foreach (var localPrice in localPlatformPrices)
        {
            if (!string.IsNullOrEmpty(localPrice.PlatformPriceId) &&
                !platformPriceIds.Contains(localPrice.PlatformPriceId))
            {
                _logger.LogDebug("Deleting price not found on platform: {PriceId}", 
                    localPrice.PlatformPriceId);
                await DeletePriceByPlatformIdAsync(localPrice.PlatformPriceId);
                operationCount++;
            }
        }

        foreach (var platformPrice in platformPriceList.Prices)
        {
            await SetPriceAsync(productId, new SetPriceDto
            {
                Platform = platform,
                PlatformPriceId = platformPrice.PriceId,
                Price = platformPrice.Price,
                Currency = platformPrice.Currency
            });
            operationCount++;
        }

        return operationCount;
    }

    public async Task MarkSyncCompletedAsync()
    {
        RaiseEvent(new PlatformPriceSyncCompletedEvent
        {
            SyncedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        
        await ConfirmEventsAsync();
    }

    #endregion

    #region General Price Management

    public async Task<PlatformPrice> SetPriceAsync(string productId, SetPriceDto dto)
    {
        _logger.LogInformation(
            "Setting price for product {ProductId}: {Price} {Currency} ({Platform})", 
            productId, dto.Price, dto.Currency, dto.Platform);

        RaiseEvent(new PriceSetEvent
        {
            ProductId = productId,
            Platform = dto.Platform,
            PlatformPriceId = dto.PlatformPriceId ?? string.Empty,
            Price = dto.Price,
            Currency = dto.Currency
        });
        
        await ConfirmEventsAsync();
        
        // Find the price we just set
        var price = GetPrice(productId, dto.PlatformPriceId, dto.Platform, dto.Currency);
        
        return price!;
    }

    public async Task DeletePriceAsync(string productId, PaymentPlatform platform, string currency)
    {
        _logger.LogInformation(
            "Deleting price for product {ProductId}: {Currency} ({Platform})", 
            productId, currency, platform);

        // Find the platform price ID if exists
        var price = GetPrice(productId, string.Empty, platform, currency);
        
        RaiseEvent(new PriceDeletedEvent
        {
            ProductId = productId,
            Platform = platform,
            Currency = currency,
            PlatformPriceId = price?.PlatformPriceId ?? string.Empty
        });
        
        await ConfirmEventsAsync();
    }

    private PlatformPrice? GetPrice(string productId, string platformPriceId, PaymentPlatform platform, string currency)
    {
        if (!State.ProductPrices.TryGetValue(productId, out var priceList))
            return null;
            
        var price = string.IsNullOrWhiteSpace(platformPriceId) 
            ? priceList.Prices.FirstOrDefault(p => p.Platform == platform && p.Currency == currency) 
            : priceList.Prices.FirstOrDefault(p => p.PlatformPriceId == platformPriceId);
            
        return price;
    }

    public async Task DeletePriceByPlatformIdAsync(string platformPriceId)
    {
        if (!State.PriceIdToProductMap.TryGetValue(platformPriceId, out _))
        {
            _logger.LogDebug("[PlatformPriceGAgent] Price not found in state, skipping delete: {PriceId}", platformPriceId);
            return;
        }

        _logger.LogInformation(
            "[PlatformPriceGAgent] Deleting price by platform ID: {PriceId}", platformPriceId);

        RaiseEvent(new PriceDeletedEvent
        {
            PlatformPriceId = platformPriceId,
        });

        await ConfirmEventsAsync();
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<PlatformPriceList> GetPricesByProductIdAsync(string productId)
    {
        if (!State.ProductPrices.TryGetValue(productId, out var priceList))
            return Task.FromResult(new PlatformPriceList());
        
        return Task.FromResult(priceList);
    }

    public Task<PlatformPrice?> GetPriceByPlatformPriceIdAsync(string platformPriceId)
    {
        if (!State.PriceIdToProductMap.TryGetValue(platformPriceId, out var productId))
            return Task.FromResult<PlatformPrice?>(null);
        
        if (!State.ProductPrices.TryGetValue(productId, out var priceList))
            return Task.FromResult<PlatformPrice?>(null);
            
        var price = priceList.Prices.FirstOrDefault(p => p.PlatformPriceId == platformPriceId);
        return Task.FromResult(price);
    }

    public Task<DateTime> GetLastPlatformPriceSyncTimeAsync()
    {
        return Task.FromResult(State.LastPlatformPriceSyncAt?.ToDateTime() ?? DateTime.MinValue);
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages platform prices and synchronization.");
    }

    #endregion

    #region State Transition

    protected override void TransitionState(PlatformPriceState state, IMessage evt)
    {
        switch (evt)
        {
            case PriceSetEvent priceSet:
                if (!state.ProductPrices.ContainsKey(priceSet.ProductId))
                    state.ProductPrices[priceSet.ProductId] = new PlatformPriceList();
                
                var priceList = state.ProductPrices[priceSet.ProductId];
                
                var existing = string.IsNullOrWhiteSpace(priceSet.PlatformPriceId)
                    ? priceList.Prices.FirstOrDefault(p => p.Platform == priceSet.Platform && p.Currency == priceSet.Currency)
                    : priceList.Prices.FirstOrDefault(p => p.PlatformPriceId == priceSet.PlatformPriceId);
                
                if (existing != null)
                {
                    existing.Price = priceSet.Price;
                    existing.PlatformPriceId = priceSet.PlatformPriceId;
                    existing.Currency = priceSet.Currency;
                    existing.LastSyncedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                }
                else
                {
                    var newPrice = new PlatformPrice
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = priceSet.ProductId,
                        Price = priceSet.Price,
                        Currency = priceSet.Currency,
                        PlatformPriceId = priceSet.PlatformPriceId,
                        Platform = priceSet.Platform,
                        LastSyncedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                    };
                    priceList.Prices.Add(newPrice);
                    
                    if (!string.IsNullOrEmpty(priceSet.PlatformPriceId))
                        state.PriceIdToProductMap[priceSet.PlatformPriceId] = priceSet.ProductId;
                }
                break;
                
            case PriceDeletedEvent priceDeleted:
                if (!string.IsNullOrEmpty(priceDeleted.PlatformPriceId) && 
                    state.PriceIdToProductMap.TryGetValue(priceDeleted.PlatformPriceId, out var productId))
                {
                    if (state.ProductPrices.TryGetValue(productId, out var platformPriceList))
                        platformPriceList.Prices.RemoveAll(p => p.PlatformPriceId == priceDeleted.PlatformPriceId);
                    state.PriceIdToProductMap.Remove(priceDeleted.PlatformPriceId);
                }
                else if (!string.IsNullOrWhiteSpace(priceDeleted.ProductId) && 
                         state.ProductPrices.TryGetValue(priceDeleted.ProductId, out var list))
                {
                    list.Prices.RemoveAll(p => p.Platform == priceDeleted.Platform && 
                                       p.Currency == priceDeleted.Currency);
                }
                break;
                
            case PlatformPriceSyncCompletedEvent syncCompleted:
                state.LastPlatformPriceSyncAt = syncCompleted.SyncedAt;
                break;
        }
    }

    #endregion

    #region Private Helpers
    
    private async Task<ISubscriptionProductGAgent> GetProductGAgentAsync()
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<SubscriptionProductGAgent>(SubscriptionGAgentKeys.ProductGAgentKey);
        return actor.As<ISubscriptionProductGAgent>();
    }

    #endregion
}
