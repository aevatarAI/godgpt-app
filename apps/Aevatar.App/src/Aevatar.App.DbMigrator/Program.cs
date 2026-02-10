using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aevatar.App.MongoDB;
using Serilog;
using Serilog.Events;

namespace Aevatar.App.DbMigrator;

class Program
{
    static async Task Main(string[] args)
    {
        // Configure MongoDB GUID serialization BEFORE any MongoDB operations
        ConfigureMongoGuidSerialization();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Volo.Abp", LogEventLevel.Warning)
#if DEBUG
                .MinimumLevel.Override("Aevatar.App", LogEventLevel.Debug)
#else
                .MinimumLevel.Override("Aevatar.App", LogEventLevel.Information)
#endif
                .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        await CreateHostBuilder(args).RunConsoleAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .AddAppSettingsSecretsJson()
            .ConfigureLogging((context, logging) => logging.ClearProviders())
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<DbMigratorHostedService>();
            });

    private static void ConfigureMongoGuidSerialization()
        => MongoGuidSerialization.Configure();
}
