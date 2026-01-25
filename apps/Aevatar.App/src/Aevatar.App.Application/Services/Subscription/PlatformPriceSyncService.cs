using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
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
    private readonly ILogger<PlatformPriceSyncService> _logger;

    public PlatformPriceSyncService(
        IGAgentActorFactory actorFactory,
        ILogger<PlatformPriceSyncService> logger)
    {
        _logger = logger;
        _actorFactory = actorFactory;
    }

    public async Task SyncAllPricesAsync()
    {
        _logger.LogInformation("Triggering full platform price sync via GAgent");
        var priceGAgent = await GetPriceGAgentAsync();
        await priceGAgent.SyncAllPricesAsync();
    }

    public async Task SyncProductPricesAsync(string subscriptionProductId)
    {
        _logger.LogInformation("Triggering platform product price sync: {ProductId}", subscriptionProductId.ToString());
        var priceGAgent = await GetPriceGAgentAsync();
        await priceGAgent.SyncProductPricesAsync(subscriptionProductId);
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
}
