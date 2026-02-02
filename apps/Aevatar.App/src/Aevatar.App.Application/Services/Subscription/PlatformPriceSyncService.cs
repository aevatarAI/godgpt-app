using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription.Providers;
using Aevatar.Application.Grains.Subscription;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service for platform price synchronization.
/// Thin wrapper around PlatformPriceGAgent - all logic is in the GAgent.
/// </summary>
public class PlatformPriceSyncService : IPlatformPriceSyncService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IPlatformPriceProviderFactory _providerFactory;
    private readonly ILogger<PlatformPriceSyncService> _logger;

    public PlatformPriceSyncService(
        IGAgentActorFactory actorFactory,
        IPlatformPriceProviderFactory providerFactory,
        ILogger<PlatformPriceSyncService> logger)
    {
        _logger = logger;
        _providerFactory = providerFactory;
        _actorFactory = actorFactory;
    }

    public async Task SyncAllPricesAsync()
    {
         _logger.LogInformation("Starting full platform price sync");
        try
        {
            var productGAgent = await GetProductGAgentAsync();
            // Get all listed products
            var productsResponse = await productGAgent.GetAllProductsAsync();
            var allProducts = productsResponse.Products.ToList();

            var priceGAgent = await GetPriceGAgentAsync();
            if (!allProducts.Any())
            {
                _logger.LogWarning("No products found for sync");
                await priceGAgent.MarkSyncCompletedAsync();
                return;
            }
            
            _logger.LogInformation("Found {Count} products to sync", allProducts.Count);

            var syncedCount = 0;
            var platforms = allProducts.Select(p => p.Platform).Distinct().ToList();
            
            foreach (var platform in platforms)
            {
                if (!_providerFactory.HasProvider(platform)) continue;
                
                var products = allProducts.Where(p => p.Platform == platform).ToList();
                var platformPriceInfoList = await _providerFactory.GetProvider(platform).GetAllPricesAsync();
                
                foreach (var product in products)
                {
                    try
                    {
                        var platformPrices = platformPriceInfoList.Prices
                            .Where(p => p.PlatformProductId == product.PlatformProductId)
                            .ToList();
                        syncedCount += await priceGAgent.SyncProductPricesFromPlatformAsync(
                            product.Id, platform, new PlatformPriceInfoList
                            {
                                Prices = { platformPrices }
                            });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error syncing prices for product {ProductId} ({Platform})",
                            product.Id, platform);
                    }
                }
            }

            await priceGAgent.MarkSyncCompletedAsync();

            _logger.LogInformation(
                "Platform price sync completed. Synced {Synced}/{Total} products",
                syncedCount, allProducts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during full platform price sync");
            throw;
        }
    }

    public async Task SyncProductPricesAsync(string subscriptionProductId)
    {
        _logger.LogInformation("Triggering platform product price sync: {ProductId}", subscriptionProductId);

        try
        {
            var productGAgent = await GetProductGAgentAsync();
            // Find our internal product by product ID
            var product = await productGAgent.GetProductAsync(subscriptionProductId);
            
            if (product == null)
            {
                _logger.LogWarning(
                    "No internal product found for product: {ProductId}", 
                    subscriptionProductId);
                return;
            }
            var provider = _providerFactory.HasProvider(product.Platform);
            if (!provider)
            {
                _logger.LogWarning(
                    "Platform {Platform} not supported for syncing prices", 
                    subscriptionProductId);
                return;
            }

            // Fetch prices via provider
            var platformPriceInfoList = await _providerFactory.GetProvider(product.Platform).GetPricesAsync(product.PlatformProductId);
            
            _logger.LogDebug(
                "Found {Count} active prices for product {ProductId}", 
                platformPriceInfoList.Prices.Count, subscriptionProductId);
            
            var priceGAgent = await GetPriceGAgentAsync();
            await priceGAgent.SyncProductPricesFromPlatformAsync(subscriptionProductId, product.Platform, platformPriceInfoList);

            _logger.LogInformation(
                "Platform product price sync completed: {ProductId}", subscriptionProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error syncing platform product prices: {ProductId}", subscriptionProductId);
            throw;
        }
    }

    public async Task<DateTime> GetLastSyncTimeAsync()
    {
        var priceGAgent = await GetPriceGAgentAsync();
        return await priceGAgent.GetLastPlatformPriceSyncTimeAsync();
    }

    private async Task<IPlatformPriceGAgent> GetPriceGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<PlatformPriceGAgent>(SubscriptionGAgentKeys.PriceGAgentKey);
            
        return actor.As<IPlatformPriceGAgent>();
    }

    private async Task<ISubscriptionProductGAgent> GetProductGAgentAsync()
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<SubscriptionProductGAgent>(SubscriptionGAgentKeys.ProductGAgentKey);
        return actor.As<ISubscriptionProductGAgent>();
    }
}
