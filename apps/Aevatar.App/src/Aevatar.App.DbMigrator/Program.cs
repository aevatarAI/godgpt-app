using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
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

    /// <summary>
    /// Configure MongoDB GUID serialization to use Legacy format for backward compatibility.
    /// Uses ConventionPack (ABP recommended approach for MongoDB Driver 3.x).
    /// Must be called before any MongoDB operations.
    /// </summary>
    private static void ConfigureMongoGuidSerialization()
    {
        try
        {
            // Register convention pack for legacy GUID handling (ABP recommended approach)
            var conventionPack = new ConventionPack { new LegacyGuidConvention() };
            ConventionRegistry.Register("LegacyGuidConvention", conventionPack, _ => true);
            Log.Information("✅ MongoDB GUID serialization configured: CSharpLegacy (Convention)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MongoDB GUID convention may already be registered, continuing...");
        }
    }
}

/// <summary>
/// Convention to serialize all GUID properties using CSharpLegacy representation.
/// This is the ABP recommended approach for MongoDB Driver 3.x compatibility.
/// </summary>
public class LegacyGuidConvention : ConventionBase, IMemberMapConvention
{
    public void Apply(BsonMemberMap memberMap)
    {
        if (memberMap.MemberType == typeof(Guid))
        {
            memberMap.SetSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
        }
        else if (memberMap.MemberType == typeof(Guid?))
        {
            var guidSerializer = new GuidSerializer(GuidRepresentation.CSharpLegacy);
            memberMap.SetSerializer(new NullableSerializer<Guid>(guidSerializer));
        }
    }
}
