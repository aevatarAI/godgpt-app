using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Demo.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

/// <summary>
/// Simple State persistence test controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StateTestController : ControllerBase
{
    private readonly IGAgentActorFactory _agentFactory;
    private readonly ILogger<StateTestController> _logger;

    public StateTestController(
        IGAgentActorFactory agentFactory,
        ILogger<StateTestController> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <summary>
    /// Test State persistence with Calculator Agent
    /// </summary>
    [HttpPost("calculator")]
    public async Task<IActionResult> TestCalculatorState(
        [FromQuery] Guid? agentId = null)
    {
        try
        {
            var id = agentId ?? Guid.NewGuid();
            _logger.LogInformation("Testing State persistence for Calculator Agent {AgentId}", id);

            // Create Agent
            var agent = await _agentFactory.CreateGAgentActorAsync<CalculatorAgent>(id);
            
            // Execute calculation via RPC (use InvokeAsync from RpcExtensions)
            var addResult = await agent.InvokeAsync<double>("AddAsync", 10.0, 5.0);
            _logger.LogInformation("Add result: {Result}", addResult);
            
            var multiplyResult = await agent.InvokeAsync<double>("MultiplyAsync", 3.0, 4.0);
            _logger.LogInformation("Multiply result: {Result}", multiplyResult);
            
            // Get state via RPC
            var lastResult = await agent.InvokeAsync<double>("GetLastResult");
            var operationCount = await agent.InvokeAsync<int>("GetOperationCount");
            
            _logger.LogInformation("Current State: LastResult={LastResult}, OperationCount={OperationCount}",
                lastResult, operationCount);

            return Ok(new
            {
                AgentId = id,
                AddResult = addResult,
                MultiplyResult = multiplyResult,
                LastResult = lastResult,
                OperationCount = operationCount,
                Message = "State should be persisted to MongoDB (check agent_states_CalculatorAgentState collection)",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestCalculatorState");
            return StatusCode(500, new { Error = ex.Message, StackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Check if State was persisted by loading a previously created Agent
    /// </summary>
    [HttpGet("calculator/{agentId}")]
    public async Task<IActionResult> GetCalculatorState(Guid agentId)
    {
        try
        {
            _logger.LogInformation("Loading Calculator Agent {AgentId}", agentId);

            // Load existing Agent (should restore state from MongoDB)
            var agent = await _agentFactory.CreateGAgentActorAsync<CalculatorAgent>(agentId);
            
            // Get state via RPC
            var lastResult = await agent.InvokeAsync<double>("GetLastResult");
            var operationCount = await agent.InvokeAsync<int>("GetOperationCount");
            
            _logger.LogInformation("Loaded State: LastResult={LastResult}, OperationCount={OperationCount}",
                lastResult, operationCount);

            return Ok(new
            {
                AgentId = agentId,
                LastResult = lastResult,
                OperationCount = operationCount,
                Message = "State loaded from MongoDB",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetCalculatorState");
            return StatusCode(500, new { Error = ex.Message, StackTrace = ex.StackTrace });
        }
    }
}
