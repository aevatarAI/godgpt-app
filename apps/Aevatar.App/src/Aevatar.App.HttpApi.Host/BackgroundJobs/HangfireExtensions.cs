using System;
using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Serilog;
using Hangfire.Server;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs;

/// <summary>
/// Extension methods for configuring Hangfire background job processing
/// </summary>
public static class HangfireExtensions
{
    /// <summary>
    /// Add Hangfire services with MongoDB storage
    /// </summary>
    public static IServiceCollection AddHangfireWithMongo(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") 
            ?? configuration.GetConnectionString("Orleans")
            ?? "mongodb://localhost:27017/AevatarBusiness";
        
        var mongoUrlBuilder = new MongoUrlBuilder(connectionString);
        var databaseName = mongoUrlBuilder.DatabaseName ?? "AevatarBusiness";

        services.AddHangfire(config =>
        {
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseMongoStorage(connectionString, databaseName, new MongoStorageOptions
                {
                    MigrationOptions = new MongoMigrationOptions
                    {
                        MigrationStrategy = new MigrateMongoMigrationStrategy(),
                        BackupStrategy = new NoneMongoBackupStrategy()
                    },
                    Prefix = "hangfire",
                    CheckConnection = true,
                    CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection
                });
        });

        Log.Information("✅ Hangfire configured with MongoDB storage: {Database}", databaseName);

        return services;
    }

    /// <summary>
    /// Configure Hangfire middleware and register recurring jobs
    /// </summary>
    public static IApplicationBuilder UseHangfireWithJobs(
        this IApplicationBuilder app,
        IHostEnvironment env)
    {
        // Use Hangfire Server to ensure JobStorage is initialized
        // This must be called before using any Hangfire APIs
        app.UseHangfireServer(new BackgroundJobServerOptions
        {
            Queues = new[] { "lumen", "default" },
            WorkerCount = Math.Max(1, Environment.ProcessorCount / 2),
            ServerName = $"aevatar-{Environment.MachineName}"
        });
        
        // Dashboard (only in development for security)
        if (env.IsDevelopment())
        {
            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                DashboardTitle = "Aevatar Background Jobs",
                DisplayStorageConnectionString = false
            });
            Log.Information("📊 Hangfire Dashboard available at /hangfire");
        }

        return app;
    }
}

