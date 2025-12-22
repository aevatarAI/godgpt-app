using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Helpers;
using Demo.Agents;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

/// <summary>
/// 展示事件溯源功能的控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventSourcingController : ControllerBase
{
    private readonly IGAgentActorFactory _agentFactory;
    private readonly IEventStore _eventStore;
    private readonly ILogger<EventSourcingController> _logger;

    public EventSourcingController(
        IGAgentActorFactory agentFactory,
        IEventStore eventStore,
        ILogger<EventSourcingController> logger)
    {
        _agentFactory = agentFactory;
        _eventStore = eventStore;
        _logger = logger;
    }

    /// <summary>
    /// 创建银行账户并执行交易
    /// </summary>
    [HttpPost("bank-account")]
    public async Task<IActionResult> BankAccountDemo(
        [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建银行账户Agent
            var accountId = Guid.NewGuid().ToString();
            var agent = await _agentFactory.CreateGAgentActorAsync<BankAccountAgent>(accountId);
            
            _logger.LogInformation("Created BankAccountAgent {AccountId} on {Runtime}", accountId, runtime);

            // 执行一系列交易
            var transactions = new List<object>();

            // 存款
            var deposit1 = new DepositEvent 
            { 
                Amount = 1000,
                Description = "Initial deposit"
            };
            var depositEnvelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(deposit1),
                Direction = EventDirection.Down
            };
            await agent.PublishEventAsync(Any.Pack(depositEnvelope), EventDirection.Down);
            transactions.Add(new { Type = "Deposit", Amount = 1000, Description = "Initial deposit" });

            // 等待处理
            await Task.Delay(100);

            // 取款
            var withdraw1 = new WithdrawEvent 
            { 
                Amount = 300,
                Description = "ATM withdrawal"
            };
            var withdrawEnvelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(withdraw1),
                Direction = EventDirection.Down
            };
            await agent.PublishEventAsync(Any.Pack(withdrawEnvelope), EventDirection.Down);
            transactions.Add(new { Type = "Withdraw", Amount = 300, Description = "ATM withdrawal" });

            // 等待处理
            await Task.Delay(100);

            // 再次存款
            var deposit2 = new DepositEvent 
            { 
                Amount = 500,
                Description = "Salary deposit"
            };
            var deposit2Envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(deposit2),
                Direction = EventDirection.Down
            };
            await agent.PublishEventAsync(Any.Pack(deposit2Envelope), EventDirection.Down);
            transactions.Add(new { Type = "Deposit", Amount = 500, Description = "Salary deposit" });

            // 等待所有交易处理完成
            await Task.Delay(500);

            return Ok(new
            {
                AccountId = accountId,
                Runtime = runtime,
                Transactions = transactions,
                ExpectedBalance = 1200, // 1000 - 300 + 500
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BankAccountDemo");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 重放事件以重建状态
    /// </summary>
    [HttpPost("replay/{agentId}")]
    public async Task<IActionResult> ReplayEvents(string agentId, [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建新的Agent实例
            var agent = await _agentFactory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
            
            _logger.LogInformation("Replaying events for Agent {AgentId} on {Runtime}", agentId, runtime);

            // 触发重放（这通常在Agent内部处理）
            // 由于IGAgentActor接口限制，我们通过发送特殊事件触发重放
            var replayTrigger = new BankAccountStateChange 
            { 
                EventType = "REPLAY_TRIGGER",
                Amount = 0,
                Description = "Trigger event replay"
            };
            
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(replayTrigger),
                Direction = EventDirection.Down
            };
            
            await agent.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);
            
            // 等待重放完成
            await Task.Delay(1000);

            return Ok(new
            {
                AgentId = agentId,
                Runtime = runtime,
                Status = "Events replayed successfully",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ReplayEvents");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 获取Agent的事件历史
    /// </summary>
    [HttpGet("history/{agentId}")]
    public async Task<IActionResult> GetEventHistory(string agentId)
    {
        try
        {
            // 从事件存储获取历史
            var events = await _eventStore.GetEventsAsync(agentId);
            
            var history = events.Select(e => new
            {
                e.EventId,
                e.AgentId,
                e.EventType,
                e.EventData,
                Timestamp = e.Timestamp
            }).ToList();

            return Ok(new
            {
                AgentId = agentId,
                EventCount = history.Count,
                Events = history,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetEventHistory");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 创建状态快照
    /// </summary>
    [HttpPost("snapshot/{agentId}")]
    public async Task<IActionResult> CreateSnapshot(string agentId, [FromQuery] string runtime = "local")
    {
        try
        {
            // 获取或创建Agent
            var agent = await _agentFactory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
            
            _logger.LogInformation("Creating snapshot for Agent {AgentId} on {Runtime}", agentId, runtime);

            // 触发快照创建（通过特殊事件）
            var snapshotTrigger = new BankAccountStateChange 
            { 
                EventType = "CREATE_SNAPSHOT",
                Amount = 0,
                Description = "Create state snapshot"
            };
            
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(snapshotTrigger),
                Direction = EventDirection.Down
            };
            
            await agent.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);
            
            // 等待快照创建
            await Task.Delay(500);

            return Ok(new
            {
                AgentId = agentId,
                Runtime = runtime,
                Status = "Snapshot created successfully",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateSnapshot");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}

// 事件类型已在 demo_messages.proto 中定义
