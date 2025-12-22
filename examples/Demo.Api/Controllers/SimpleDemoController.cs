using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Demo.Agents;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

/// <summary>
/// 简化的Demo控制器，展示框架基本功能
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SimpleDemoController : ControllerBase
{
    private readonly IGAgentActorFactory _agentFactory;
    private readonly ILogger<SimpleDemoController> _logger;

    public SimpleDemoController(
        IGAgentActorFactory agentFactory,
        ILogger<SimpleDemoController> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <summary>
    /// 测试基本的Agent创建和事件发布
    /// </summary>
    [HttpPost("basic")]
    public async Task<IActionResult> BasicDemo(
        [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建Agent
            var agentId = Guid.NewGuid().ToString();
            var agent = await _agentFactory.CreateGAgentActorAsync<WeatherAgent>(agentId);
            
            _logger.LogInformation("Created agent {AgentId} on {Runtime}", agentId, runtime);

            // 发送事件
            var weatherUpdate = new StringValue { Value = "Sunny, 25°C" };
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(weatherUpdate),
                Direction = EventDirection.Down
            };

            var eventId = await agent.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);

            return Ok(new
            {
                AgentId = agentId,
                Runtime = runtime,
                EventId = eventId,
                Message = "Weather update sent",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BasicDemo");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 测试父子层级关系
    /// </summary>
    [HttpPost("hierarchy")]
    public async Task<IActionResult> HierarchyDemo(
        [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建父子Agent
            var parentId = Guid.NewGuid().ToString();
            var childId = Guid.NewGuid().ToString();
            
            var parent = await _agentFactory.CreateGAgentActorAsync<WeatherAgent>(parentId);
            var child = await _agentFactory.CreateGAgentActorAsync<WeatherAgent>(childId);
            
            // 建立层级关系
            await ActorHierarchyCoordinator.LinkAsync(parent, child, _logger);
            
            _logger.LogInformation("Created hierarchy: Parent {ParentId} -> Child {ChildId} on {Runtime}", 
                parentId, childId, runtime);

            // 从子节点向上发送事件
            var message = new StringValue { Value = "Message from child" };
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(message),
                Direction = EventDirection.Up
            };

            var eventId = await child.PublishEventAsync(Any.Pack(envelope), EventDirection.Up);

            return Ok(new
            {
                ParentId = parentId,
                ChildId = childId,
                Runtime = runtime,
                EventId = eventId,
                Direction = "Up",
                Message = "Event sent from child to parent",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HierarchyDemo");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 测试事件广播
    /// </summary>
    [HttpPost("broadcast")]
    public async Task<IActionResult> BroadcastDemo(
        [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建一个小型树结构
            var rootId = Guid.NewGuid().ToString();
            var child1Id = Guid.NewGuid().ToString();
            var child2Id = Guid.NewGuid().ToString();
            
            var root = await _agentFactory.CreateGAgentActorAsync<WeatherAgent>(rootId);
            var child1 = await _agentFactory.CreateGAgentActorAsync<WeatherAgent>(child1Id);
            var child2 = await _agentFactory.CreateGAgentActorAsync<WeatherAgent>(child2Id);
            
            // 建立层级关系
            await ActorHierarchyCoordinator.LinkAsync(root, child1, _logger);
            await ActorHierarchyCoordinator.LinkAsync(root, child2, _logger);
            
            _logger.LogInformation("Created tree structure on {Runtime}", runtime);

            // 从根节点向下广播
            var broadcast = new StringValue { Value = "Broadcast message to all children" };
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(broadcast),
                Direction = EventDirection.Down
            };

            var eventId = await root.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);

            return Ok(new
            {
                RootId = rootId,
                Children = new[] { child1Id, child2Id },
                Runtime = runtime,
                EventId = eventId,
                Direction = "Down",
                Message = "Event broadcast to all children",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BroadcastDemo");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 获取运行时信息
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetRuntimeInfo([FromServices] IConfiguration configuration)
    {
        var runtimeOptions = configuration
            .GetSection("AgentRuntime")
            .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

        return Ok(new
        {
            CurrentRuntime = runtimeOptions.RuntimeType.ToString(),
            AvailableRuntimes = new[] { "Local", "ProtoActor", "Orleans" },
            OrleansConfig = runtimeOptions.RuntimeType == AgentRuntimeType.Orleans ? new
            {
                runtimeOptions.Orleans.ClusterId,
                runtimeOptions.Orleans.ServiceId,
                runtimeOptions.Orleans.UseLocalhostClustering,
                runtimeOptions.Orleans.SiloPort,
                runtimeOptions.Orleans.GatewayPort
            } : null,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
