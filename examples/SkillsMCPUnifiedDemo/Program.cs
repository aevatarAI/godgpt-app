using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkillsMCPUnifiedDemo;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, config) =>
            {
                // dotnet run --project may keep CWD at repo root, so load both relative + output dir.
                var baseDir = AppContext.BaseDirectory;

                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(baseDir, "appsettings.json"), optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(baseDir, "appsettings.secrets.json"), optional: true, reloadOnChange: false);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;

                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // LLM providers
                services.Configure<LLMProvidersConfig>(config.GetSection("LLMProviders"));
                services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();

                // Local runtime (agent factory + injectors)
                services.AddAevatarLocalRuntime();
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SkillsMCPUnifiedDemo");
        var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

        logger.LogInformation("╔════════════════════════════════════════════╗");
        logger.LogInformation("║       Skills + MCP Unified Demo            ║");
        logger.LogInformation("╚════════════════════════════════════════════╝");

        var actor = await actorFactory.CreateGAgentActorAsync<UnifiedAgent>();
        var agent = (UnifiedAgent)actor.GetAgent();

        // Init LLM
        logger.LogInformation("▶ Initializing LLM provider '{Provider}' ...", AevatarAgentsConstants.DefaultProviderName);
        try
        {
            await agent.InitializeAsync(
                AevatarAgentsConstants.DefaultProviderName,
                cfg =>
                {
                    cfg.Model = "deepseek-chat";
                    cfg.Temperature = 0.2f;
                    cfg.MaxOutputTokens = 900;
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LLM init failed. Add API key to examples/SkillsMCPUnifiedDemo/appsettings.secrets.json then retry.");
            return;
        }

        logger.LogInformation("✅ LLM initialized.");

        // Scripted prompts (shows: skills_list/skills_load -> allowlist -> dotnet-file tool)
        await RunChatAsync(logger, agent, "请先调用 skills_list，然后 skills_load 加载 time-helper skill，再按 skill 的步骤回答：现在几点？");
        await RunChatAsync(logger, agent, "用 system_info 给我一个系统信息摘要（os/framework/pid）。");

        logger.LogInformation("\n▶ Interactive chat (type 'exit' to quit) ...");
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\nUser> ");
            Console.ResetColor();

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            await RunChatAsync(logger, agent, input);
        }

        logger.LogInformation("\nDone.");
    }

    private static async Task RunChatAsync(
        ILogger logger,
        UnifiedAgent agent,
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = new Aevatar.Agents.AI.ChatRequest
        {
            Message = message,
            RequestId = Guid.NewGuid().ToString("N")
        };

        logger.LogInformation("\nUser: {Message}", message);
        var response = await agent.ChatAsync(request, cancellationToken);
        logger.LogInformation("Assistant: {Content}", response.Content);

        if (response.ToolCalled)
        {
            var args = response.ToolCall?.Arguments != null
                ? string.Join(", ", response.ToolCall.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                : string.Empty;

            logger.LogInformation("[ToolCalled] {Tool}({Args}) -> {Result}",
                response.ToolCall?.ToolName,
                args,
                response.ToolCall?.Result);
        }
    }
}

