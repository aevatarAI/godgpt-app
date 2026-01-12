using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MigrationTool.Converters;
using MigrationTool.Converters.UserStatistics;
using MigrationTool.Models;
using MigrationTool.Readers;
using MigrationTool.Readers.UserStatistics;
using MigrationTool.Services;
using MigrationTool.Writers;
using MongoDB.Driver;
using Spectre.Console;

namespace MigrationTool;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Banner
        AnsiConsole.Write(
            new FigletText("Migration Tool")
                .LeftJustified()
                .Color(Color.Green));
        
        AnsiConsole.MarkupLine("[grey]GodGPT Data Migration Tool v1.0[/]");
        AnsiConsole.WriteLine();

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        // Build services
        var services = ConfigureServices(configuration);

        // Parse command
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLower();
        
        try
        {
            return command switch
            {
                "migrate-one" => await MigrateOneAsync(services, args),
                "migrate-all" => await MigrateAllAsync(services, args),
                "verify" => await VerifyAsync(services, args),
                "count" => await CountAsync(services, args),
                "help" or "--help" or "-h" => ShowHelp(),
                _ => ShowUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    static IServiceProvider ConfigureServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // MongoDB - Old database
        var oldConnStr = configuration["OldMongoDB:ConnectionString"] ?? "mongodb://localhost:27017";
        var oldDbName = configuration["OldMongoDB:Database"] ?? "godgpt_old";
        var oldCollection = configuration["OldMongoDB:GrainStateCollection"] ?? "OrleansGrainState";
        
        var oldClient = new MongoClient(oldConnStr);
        var oldDb = oldClient.GetDatabase(oldDbName);
        services.AddSingleton<IMongoDatabase>(sp => oldDb);
        services.AddKeyedSingleton("old", oldDb);
        
        // MongoDB - New database
        var newConnStr = configuration["NewMongoDB:ConnectionString"] ?? "mongodb://localhost:27017";
        var newDbName = configuration["NewMongoDB:Database"] ?? "godgpt_new";
        
        var newClient = new MongoClient(newConnStr);
        var newDb = newClient.GetDatabase(newDbName);
        services.AddKeyedSingleton("new", newDb);

        // UserStatistics components
        services.AddSingleton<IOldStateReader<OldUserStatisticsState>>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<UserStatisticsOldReader>>();
            return new UserStatisticsOldReader(oldDb, oldCollection, logger);
        });
        
        services.AddSingleton<IStateConverter<OldUserStatisticsState, UserStatisticsState>, UserStatisticsConverter>();
        
        services.AddSingleton<INewStateWriter<UserStatisticsState>>(sp =>
        {
            return new MongoDBStateWriter<UserStatisticsState>(newDb);
        });

        // Migration service
        services.AddSingleton(sp =>
        {
            var reader = sp.GetRequiredService<IOldStateReader<OldUserStatisticsState>>();
            var converter = sp.GetRequiredService<IStateConverter<OldUserStatisticsState, UserStatisticsState>>();
            var writer = sp.GetRequiredService<INewStateWriter<UserStatisticsState>>();
            var logger = sp.GetRequiredService<ILogger<MigrationService<OldUserStatisticsState, UserStatisticsState>>>();
            return new MigrationService<OldUserStatisticsState, UserStatisticsState>(reader, converter, writer, logger);
        });

        // Verification service
        services.AddSingleton(sp =>
        {
            var reader = sp.GetRequiredService<IOldStateReader<OldUserStatisticsState>>();
            var writer = sp.GetRequiredService<INewStateWriter<UserStatisticsState>>();
            var converter = sp.GetRequiredService<IStateConverter<OldUserStatisticsState, UserStatisticsState>>();
            var logger = sp.GetRequiredService<ILogger<VerificationService<OldUserStatisticsState, UserStatisticsState>>>();
            return new VerificationService<OldUserStatisticsState, UserStatisticsState>(reader, writer, converter, logger);
        });

        return services.BuildServiceProvider();
    }

    static async Task<int> MigrateOneAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 3 || args[1] != "--agent-id")
        {
            AnsiConsole.MarkupLine("[red]Usage: migrate-one --agent-id <id>[/]");
            return 1;
        }

        var agentId = args[2];
        AnsiConsole.MarkupLine($"[yellow]Migrating agent:[/] {agentId}");

        var migrationService = services.GetRequiredService<MigrationService<OldUserStatisticsState, UserStatisticsState>>();
        var success = await migrationService.MigrateOneAsync(agentId);

        if (success)
        {
            AnsiConsole.MarkupLine("[green]✓ Migration successful![/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine("[red]✗ Migration failed![/]");
            return 1;
        }
    }

    static async Task<int> MigrateAllAsync(IServiceProvider services, string[] args)
    {
        var migrationService = services.GetRequiredService<MigrationService<OldUserStatisticsState, UserStatisticsState>>();
        var reader = services.GetRequiredService<IOldStateReader<OldUserStatisticsState>>();
        
        var total = await reader.GetCountAsync();
        AnsiConsole.MarkupLine($"[yellow]Found {total} records to migrate[/]");
        
        if (!AnsiConsole.Confirm("Proceed with migration?"))
        {
            AnsiConsole.MarkupLine("[grey]Migration cancelled.[/]");
            return 0;
        }

        var result = await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Migrating...[/]");
                task.MaxValue = total;

                var progress = new Progress<(int Current, int Total)>(p =>
                {
                    task.Value = p.Current;
                });

                return await migrationService.MigrateAllAsync(progress: progress);
            });

        // Display results
        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");
        
        table.AddRow("Total", result.TotalCount.ToString());
        table.AddRow("Success", $"[green]{result.SuccessCount}[/]");
        table.AddRow("Failed", result.FailedIds.Count > 0 ? $"[red]{result.FailedIds.Count}[/]" : "0");
        table.AddRow("Success Rate", $"{result.SuccessRate:F2}%");
        table.AddRow("Duration", result.Duration.ToString(@"hh\:mm\:ss"));
        
        AnsiConsole.Write(table);

        if (result.FailedIds.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[red]Failed IDs:[/]");
            foreach (var id in result.FailedIds.Take(10))
            {
                AnsiConsole.MarkupLine($"  - {id}");
            }
            if (result.FailedIds.Count > 10)
            {
                AnsiConsole.MarkupLine($"  ... and {result.FailedIds.Count - 10} more");
            }
        }

        return result.FailedIds.Count > 0 ? 1 : 0;
    }

    static async Task<int> VerifyAsync(IServiceProvider services, string[] args)
    {
        var verificationService = services.GetRequiredService<VerificationService<OldUserStatisticsState, UserStatisticsState>>();
        
        var sampleSize = 100;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--sample-size" && i + 1 < args.Length)
            {
                sampleSize = int.Parse(args[i + 1]);
            }
            else if (args[i] == "--agent-id" && i + 1 < args.Length)
            {
                // Verify single agent
                var agentId = args[i + 1];
                var oneResult = await verificationService.VerifyOneAsync(agentId);
                AnsiConsole.MarkupLine($"Agent {agentId}: [{GetResultColor(oneResult)}]{oneResult}[/]");
                return oneResult == VerifyOneResult.Match ? 0 : 1;
            }
        }

        AnsiConsole.MarkupLine($"[yellow]Verifying sample of {sampleSize} records...[/]");
        
        var result = await verificationService.VerifySampleAsync(sampleSize);

        // Display results
        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");
        
        table.AddRow("Total Verified", result.TotalVerified.ToString());
        table.AddRow("Matched", $"[green]{result.MatchCount}[/]");
        table.AddRow("Mismatched", result.MismatchIds.Count > 0 ? $"[red]{result.MismatchIds.Count}[/]" : "0");
        table.AddRow("Missing in New", result.MissingInNew.Count > 0 ? $"[yellow]{result.MissingInNew.Count}[/]" : "0");
        table.AddRow("Errors", result.ErrorIds.Count > 0 ? $"[red]{result.ErrorIds.Count}[/]" : "0");
        table.AddRow("Match Rate", $"{result.MatchRate:F2}%");
        
        AnsiConsole.Write(table);

        return result.MatchRate >= 99.0 ? 0 : 1;
    }

    static async Task<int> CountAsync(IServiceProvider services, string[] args)
    {
        var reader = services.GetRequiredService<IOldStateReader<OldUserStatisticsState>>();
        
        AnsiConsole.MarkupLine("[yellow]Counting records...[/]");
        var count = await reader.GetCountAsync();
        
        AnsiConsole.MarkupLine($"[green]Found {count} UserStatistics records in old database[/]");
        
        return 0;
    }

    static int ShowHelp()
    {
        var helpText = new Panel(
            new Markup(
                "[bold]Commands:[/]\n\n" +
                "  [green]migrate-one[/] --agent-id <id>    Migrate a single agent\n" +
                "  [green]migrate-all[/]                    Migrate all agents\n" +
                "  [green]verify[/] [--sample-size N]       Verify migrated data\n" +
                "  [green]verify[/] --agent-id <id>         Verify single agent\n" +
                "  [green]count[/]                          Count records in old database\n" +
                "  [green]help[/]                           Show this help\n\n" +
                "[bold]Configuration:[/]\n\n" +
                "  Edit [yellow]appsettings.json[/] to configure database connections"))
            .Header("[bold]GodGPT Migration Tool[/]")
            .Border(BoxBorder.Rounded);
        
        AnsiConsole.Write(helpText);
        return 0;
    }

    static int ShowUnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command: {command}[/]");
        AnsiConsole.MarkupLine("Use [yellow]help[/] to see available commands.");
        return 1;
    }

    static string GetResultColor(VerifyOneResult result) => result switch
    {
        VerifyOneResult.Match => "green",
        VerifyOneResult.Mismatch => "red",
        VerifyOneResult.MissingInNew => "yellow",
        VerifyOneResult.Error => "red",
        _ => "white"
    };
}
