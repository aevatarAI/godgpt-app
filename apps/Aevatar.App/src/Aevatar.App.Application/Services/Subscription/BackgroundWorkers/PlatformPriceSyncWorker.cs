using System;
using System.Threading.Tasks;
using Aevatar.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace Aevatar.App.Services.Subscription.BackgroundWorkers;

/// <summary>
/// Background worker for periodic platform price synchronization.
/// Uses ABP's built-in scoped service resolution pattern.
/// </summary>
public class PlatformPriceSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    private readonly PlatformPriceSyncOptions _options;

    public PlatformPriceSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptionsMonitor<PlatformPriceSyncOptions> options)
        : base(timer, serviceScopeFactory)
    {
        _options = options.CurrentValue;

        // Set timer period: default 24 hours
        Timer.Period = _options.SyncIntervalSeconds * 1000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        if (!_options.IsEnabled)
        {
            Logger.LogDebug("Platform price sync is disabled");
            return;
        }

        Logger.LogInformation("Starting scheduled platform price sync");

        try
        {
            var syncService = workerContext.ServiceProvider
                .GetRequiredService<IPlatformPriceSyncService>();

            await syncService.SyncAllPricesAsync();

            Logger.LogInformation("Scheduled platform price sync completed");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Scheduled platform price sync failed");
            // Don't rethrow - let the worker continue running
        }
    }
}
