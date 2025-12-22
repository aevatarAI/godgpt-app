using Aevatar.Agents.Abstractions;
using Demo.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CalculatorController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;

    public CalculatorController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 加法运算
    /// </summary>
    [HttpPost("add")]
    public async Task<IActionResult> Add([FromQuery] double a, [FromQuery] double b)
    {
        return await ExecuteOperation(async agent => await agent.AddAsync(a, b), $"{a} + {b}");
    }

    /// <summary>
    /// 减法运算
    /// </summary>
    [HttpPost("subtract")]
    public async Task<IActionResult> Subtract([FromQuery] double a, [FromQuery] double b)
    {
        return await ExecuteOperation(async agent => await agent.SubtractAsync(a, b), $"{a} - {b}");
    }

    /// <summary>
    /// 乘法运算
    /// </summary>
    [HttpPost("multiply")]
    public async Task<IActionResult> Multiply([FromQuery] double a, [FromQuery] double b)
    {
        return await ExecuteOperation(async agent => await agent.MultiplyAsync(a, b), $"{a} × {b}");
    }

    /// <summary>
    /// 除法运算
    /// </summary>
    [HttpPost("divide")]
    public async Task<IActionResult> Divide([FromQuery] double a, [FromQuery] double b)
    {
        return await ExecuteOperation(async agent => await agent.DivideAsync(a, b), $"{a} ÷ {b}");
    }

    /// <summary>
    /// 获取API信息
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        try
        {
            return Ok(new
            {
                Message = "Calculator Agent API",
                Version = "1.0.0",
                Description = "执行基本数学运算",
                Operations = new[] { "add", "subtract", "multiply", "divide" }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    private async Task<IActionResult> ExecuteOperation(
        Func<CalculatorAgent, Task<double>> operation,
        string operationDescription)
    {
        try
        {
            // 通过 ActorFactory 创建 Actor
            var factory = _serviceProvider.GetRequiredService<IGAgentActorFactory>();
            var agentActor = await factory.CreateGAgentActorAsync<CalculatorAgent>(Guid.NewGuid().ToString());

            // 获取 Agent 并执行业务逻辑
            var calculatorAgent = (CalculatorAgent)agentActor.GetAgent();

            // 执行操作
            var result = await operation(calculatorAgent);

            // 获取历史记录
            var history = calculatorAgent.GetHistory();

            // 清理
            await agentActor.DeactivateAsync();

            return Ok(new
            {
                Operation = operationDescription,
                Result = result,
                AgentId = agentActor.Id,
                History = history
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}
