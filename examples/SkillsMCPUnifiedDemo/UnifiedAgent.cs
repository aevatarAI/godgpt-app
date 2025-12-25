using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool.MCP;
using Aevatar.Agents.AI.WithTool.MCP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SkillsMCPUnifiedDemo;

/// <summary>
/// A single agent that demonstrates:
/// - dotnet-file tools (skills/*.cs)
/// - Agent Skills (agent_skills/**/SKILL.md) via skills_list/skills_load
/// - MCP tools (docker filesystem / context7) best-effort
/// </summary>
public sealed class UnifiedAgent : AIGAgentBase
{
    private readonly IConfiguration _configuration;

    public UnifiedAgent(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Agent Skills
        EnableAgentSkills = true;
        AddAgentSkillsRoot(Path.Combine(AppContext.BaseDirectory, "agent_skills"));

        SystemPrompt =
            "You are a helpful AI assistant.\n" +
            "If a relevant Agent Skill exists, you MUST call skills_list then skills_load before acting.\n" +
            "For runtime/system information, prefer dotnet-file tools.\n" +
            "For external context, prefer MCP tools when available.";
    }

    public override Task<string> GetDescriptionAsync() => Task.FromResult("SkillsMCPUnifiedDemoAgent");

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        // dotnet-file tools (skills/*.cs)
        var skillsDir = Path.Combine(AppContext.BaseDirectory, "skills");
        await RegisterDotNetFileSkillAsync(Path.Combine(skillsDir, "get_time.cs"), cancellationToken);
        await RegisterDotNetFileSkillAsync(Path.Combine(skillsDir, "system_info.cs"), cancellationToken);

        // MCP servers (best-effort)
        await RegisterMcpServersBestEffortAsync(cancellationToken);
    }

    private async Task RegisterMcpServersBestEffortAsync(CancellationToken cancellationToken)
    {
        // 1) Docker filesystem MCP (optional)
        try
        {
            var dockerConfig = MCPServerConfig.CreateDockerConfig(
                imageName: "mcp/server-filesystem:latest",
                name: "Docker Filesystem MCP",
                args: new List<string> { "/tmp" } // inside container
            );

            var dockerClient = await MCPClientWrapper.CreateAsync(dockerConfig, Logger, cancellationToken);
            await ToolManager.RegisterMCPServerAsync("docker-fs", dockerClient, dockerConfig, Logger, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("Skipping Docker filesystem MCP (optional): {Message}", ex.Message);
        }

        // 2) Context7 HTTP MCP (optional)
        try
        {
            var context7 = _configuration.GetSection("Context7");
            var mcpUrl = context7["McpUrl"];
            var apiKey = context7["ApiKey"];
            var apiUrl = context7["ApiUrl"]; // optional, may be used by some deployments

            if (string.IsNullOrWhiteSpace(mcpUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                Logger?.LogInformation("Context7 not configured, skipping Context7 MCP.");
                return;
            }

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(apiUrl))
            {
                headers["CONTEXT7_API_URL"] = apiUrl;
            }

            var httpConfig = MCPServerConfig.CreateHttpConfig(
                serverUrl: mcpUrl,
                authToken: apiKey,
                name: "Context7 MCP",
                headers: headers.Count > 0 ? headers : null
            );

            var httpClient = await MCPClientWrapper.CreateAsync(httpConfig, Logger, cancellationToken);
            await ToolManager.RegisterMCPServerAsync("context7", httpClient, httpConfig, Logger, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning("Skipping Context7 MCP (optional): {Message}", ex.Message);
        }
    }
}

