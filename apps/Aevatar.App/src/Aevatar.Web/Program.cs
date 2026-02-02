using System;
using System.Threading.Tasks;
using Aevatar.App.HttpApi.Host.Extensions;
using Aevatar.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers.MongoDB.Configuration;
using Orleans.Serialization;
using Serilog;
using Serilog.Events;

namespace Aevatar.Web;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        try
        {
            Log.Information("Starting Aevatar Web host.");
            var builder = WebApplication.CreateBuilder(args);
            
            // Read Agent Runtime configuration
            var runtimeOptions = builder.Configuration
                .GetSection(AgentRuntimeOptions.SectionName)
                .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();
            
            // Configure Orleans if using Orleans runtime
            if (runtimeOptions.RuntimeType == AgentRuntimeType.Orleans)
            {
                ConfigureOrleans(builder, runtimeOptions.Orleans);
            }
            
            builder.Host.AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();
            await builder.AddApplicationAsync<AevatarWebModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
    
    /// <summary>
    /// Configure Orleans when using Orleans runtime
    /// Matches Legacy Silo configuration (MongoDB Clustering)
    /// </summary>
    private static void ConfigureOrleans(WebApplicationBuilder builder, OrleansRuntimeOptions orleansOptions)
    {
        builder.Host.UseOrleansClient((context, clientBuilder) =>
        {
            var config = context.Configuration;
            // Use Orleans connection string for MongoDB clustering
            var connectionString = config.GetConnectionString("Orleans") ?? "mongodb://localhost:27017/AevatarBusiness";
            
            // Get database name from config, or parse from connection string
            var databaseName = config.GetSection("Storage")
                .GetValue("DatabaseName", string.Empty);
            
            if (string.IsNullOrEmpty(databaseName))
            {
                // Parse database name from connection string as fallback
                var mongoUrl = new MongoUrl(connectionString);
                databaseName = mongoUrl.DatabaseName ?? "AevatarBusiness";
            }
            
            Log.Information("🌐 Configuring Orleans Client with MongoDB Clustering");
            Log.Information("   ConnectionString: {ConnectionString}", connectionString);
            Log.Information("   DatabaseName: {DatabaseName}", databaseName);

            // 1. Configure MongoDB Client
            clientBuilder.UseMongoDBClient(connectionString);

            // 2. Configure Clustering (Must match Silo)
            clientBuilder.UseMongoDBClustering(options =>
            {
                options.DatabaseName = databaseName;
                options.Strategy = MongoDBMembershipStrategy.SingleDocument;
                options.CollectionPrefix = "OrleansAevatar"; 
            });

            // 3. Configure Cluster Options
            clientBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = orleansOptions.ClusterId;
                options.ServiceId = orleansOptions.ServiceId;
            });

            // 4. Add Protobuf serializer
            clientBuilder.ConfigureServices(services =>
            {
                services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddProtobufSerializer();
                });
            });
            
            Log.Information("   ClusterId: {ClusterId}", orleansOptions.ClusterId);
            Log.Information("   ServiceId: {ServiceId}", orleansOptions.ServiceId);
        });
    }
}

