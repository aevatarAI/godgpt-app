using System;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.App.Agents.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// Agent Demo Controller
/// Demonstrates Aevatar Agent Framework integration
/// 
/// NOTE: In Orleans mode, Agent runs in Silo (Grain).
/// All operations go through Actor proxy which forwards to Grain via RPC.
/// </summary>
[Route("api/agent-demo")]
[ApiController]
public class AgentDemoController : AbpControllerBase
{
    private readonly IGAgentActorManager _actorManager;
    private readonly ILogger<AgentDemoController> _logger;

    public AgentDemoController(
        IGAgentActorManager actorManager,
        ILogger<AgentDemoController> logger)
    {
        _actorManager = actorManager;
        _logger = logger;
    }

    /// <summary>
    /// Create a new agent
    /// </summary>
    /// <returns>Agent creation response with ID and description</returns>
    [HttpPost("agents")]
    public async Task<ActionResult<AgentCreatedResponse>> CreateAgent()
    {
        var agentId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("🚀 Creating agent with ID: {AgentId}", agentId);

        try
        {
            // Create and register agent actor (Agent is created in Silo/Grain)
            var actor = await _actorManager.CreateAndRegisterAsync<SimpleBusinessAgent>(agentId);

            // Get description via Actor proxy (calls Grain RPC)
            var description = await actor.GetDescriptionAsync();

            _logger.LogInformation("✅ Agent {AgentId} created successfully (running in Silo)", agentId);

            return Ok(new AgentCreatedResponse
            {
                AgentId = agentId.ToString(),
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send message to agent
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="request">Message request</param>
    /// <returns>Agent response</returns>
    [HttpPost("agents/{agentId}/messages")]
    public async Task<ActionResult<AgentMessageResponse>> SendMessage(
        [FromRoute] string agentId,
        [FromBody] AgentMessageRequest request)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        _logger.LogInformation("📨 Sending message to agent {AgentId}: {Message}", 
            agentId, request.Message);

        try
        {
            // Get existing agent or create if not exists
            var actor = await _actorManager.GetActorAsync(agentId);
            if (actor == null)
            {
                actor = await _actorManager.CreateAndRegisterAsync<SimpleBusinessAgent>(agentId);
            }

            // Send event to Agent for processing (in Silo/Grain)
            var evt = new Business.Server.BusinessMessageEvent
            {
                Message = request.Message,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            // PublishEventAsync now correctly sets empty PublisherId for external calls
            await actor.PublishEventAsync(evt, EventDirection.Down);

            // Get updated description (reflects state changes)
            var description = await actor.GetDescriptionAsync();

            _logger.LogInformation("✅ Message sent to agent (processed in Silo)");

            return Ok(new AgentMessageResponse
            {
                AgentId = agentId,
                Response = description,
                ProcessedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing message for agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get agent statistics
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <returns>Agent statistics</returns>
    [HttpGet("agents/{agentId}/stats")]
    public async Task<ActionResult<AgentStatsResponse>> GetStatistics([FromRoute] string agentId)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        _logger.LogInformation("📊 Getting stats for agent {AgentId}", agentId);

        try
        {
            // Get existing agent
            var actor = await _actorManager.GetActorAsync(agentId);
            if (actor == null)
            {
                return NotFound($"Agent {agentId} not found");
            }

            // Get description via Grain RPC (contains stats info)
            var description = await actor.GetDescriptionAsync();

            return Ok(new AgentStatsResponse
            {
                AgentId = agentId,
                ProcessedEventsCount = 0, // Stats are embedded in description
                LastMessage = description,
                LastUpdated = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting stats for agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Set parent for agent (establishes hierarchy)
    /// </summary>
    [HttpPost("agents/{childId}/parent/{parentId}")]
    public async Task<IActionResult> SetParent([FromRoute] string childId, [FromRoute] string parentId)
    {
        if (!Guid.TryParse(childId, out var cId) || !Guid.TryParse(parentId, out var pId))
        {
            return BadRequest("Invalid ID format");
        }

        try
        {
            var childActor = await _actorManager.GetActorAsync(childId);
            if (childActor == null) return NotFound($"Child agent {childId} not found");

            var parentActor = await _actorManager.GetActorAsync(parentId);
            if (parentActor == null) return NotFound($"Parent agent {parentId} not found");

            // Establish bidirectional relationship using ActorHierarchyCoordinator
            await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger);
            
            _logger.LogInformation("✅ Parent-child relationship established: {Child} -> {Parent}", childId, parentId);
            return Ok(new { message = $"Child {childId} now has parent {parentId}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error setting parent");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Publish event to agent's stream
    /// </summary>
    [HttpPost("agents/{agentId}/events")]
    public async Task<IActionResult> PublishEvent(
        [FromRoute] string agentId,
        [FromBody] AgentEventRequest request)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        try
        {
            var actor = await _actorManager.GetActorAsync(agentId);
            if (actor == null) return NotFound($"Agent {agentId} not found");

            // Create event and publish via Actor proxy (processed in Silo/Grain)
            var evt = new Business.Server.BusinessMessageEvent
            {
                Message = request.Message,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await actor.PublishEventAsync(evt, EventDirection.Down);
            
            _logger.LogInformation("✅ Event published to agent {AgentId} (processed in Silo)", agentId);
            return Ok(new { message = "Event published successfully", eventData = request.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error publishing event");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Test agent health
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }

    // ========== Complex State Agent Endpoints (for CQRS ES testing) ==========
    // TODO: ComplexStateAgent has been removed during migration cleanup
    // These endpoints were for testing complex state handling
    // Uncomment and fix when needed for testing
    /*
    [HttpPost("complex-agent")]
    public async Task<ActionResult<ComplexAgentCreatedResponse>> CreateComplexAgent()
    {
        var agentId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("🧪 Creating ComplexStateAgent with ID: {AgentId}", agentId);

        try
        {
            // Create and register complex state agent
            var actor = await _actorManager.CreateAndRegisterAsync<ComplexStateAgent>(agentId);

            // Get the agent instance to initialize test data
            // Note: In Local mode, we can get the agent directly
            // In Orleans mode, we need to call through Actor/Grain RPC
            var agent = actor.GetAgent() as ComplexStateAgent;
            if (agent != null)
            {
                await agent.InitializeTestDataAsync();
            }
            else
            {
                _logger.LogWarning("Could not get agent instance directly (Orleans mode). " +
                    "Use /complex-agent/{id}/init to initialize test data.");
            }

            var description = await actor.GetDescriptionAsync();

            _logger.LogInformation("✅ ComplexStateAgent {AgentId} created with test data", agentId);

            return Ok(new ComplexAgentCreatedResponse
            {
                AgentId = agentId.ToString(),
                Description = description,
                AgentType = "Aevatar.App.Agents.Agents.ComplexStateAgent",
                CreatedAt = DateTime.UtcNow,
                TestDataInitialized = agent != null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating ComplexStateAgent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Initialize test data for existing complex agent (for Orleans mode).
    /// </summary>
    [HttpPost("complex-agent/{agentId}/init")]
    public async Task<IActionResult> InitComplexAgentTestData([FromRoute] string agentId)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        try
        {
            var actor = await _actorManager.GetActorAsync(agentId);
            if (actor == null)
            {
                return NotFound($"Agent {agentId} not found");
            }

            var agent = actor.GetAgent() as ComplexStateAgent;
            if (agent != null)
            {
                await agent.InitializeTestDataAsync();
                return Ok(new { message = "Test data initialized", agentId });
            }
            else
            {
                return BadRequest("Agent is not a ComplexStateAgent or not accessible (Orleans mode)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error initializing test data for agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get complex agent state (for verification).
    /// </summary>
    [HttpGet("complex-agent/{agentId}/state")]
    public async Task<ActionResult<object>> GetComplexAgentState([FromRoute] string agentId)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        try
        {
            var actor = await _actorManager.GetActorAsync(agentId);
            if (actor == null)
            {
                return NotFound($"Agent {agentId} not found");
            }

            var agent = actor.GetAgent() as ComplexStateAgent;
            if (agent != null)
            {
                var state = await agent.GetStateAsync();
                return Ok(new
                {
                    agentId = state.AgentId,
                    name = state.Name,
                    age = state.Age,
                    balance = state.Balance,
                    isActive = state.IsActive,
                    address = state.Address != null ? new
                    {
                        street = state.Address.Street,
                        city = state.Address.City,
                        country = state.Address.Country,
                        zipCode = state.Address.ZipCode
                    } : null,
                    tags = state.Tags.ToList(),
                    orders = state.Orders.Select(o => new
                    {
                        productId = o.ProductId,
                        productName = o.ProductName,
                        quantity = o.Quantity,
                        price = o.Price
                    }).ToList(),
                    metadata = state.Metadata.ToDictionary(x => x.Key, x => x.Value),
                    scores = state.Scores.ToDictionary(x => x.Key, x => x.Value),
                    luckyNumbers = state.LuckyNumbers.ToList(),
                    createdAt = state.CreatedAt?.ToDateTime(),
                    lastUpdated = state.LastUpdated?.ToDateTime()
                });
            }
            else
            {
                var description = await actor.GetDescriptionAsync();
                return Ok(new { description, message = "Full state not accessible in Orleans mode" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting state for agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }
    */
}

// ========== DTOs ==========

public class AgentCreatedResponse
{
    public string AgentId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AgentMessageRequest
{
    public string Message { get; set; } = string.Empty;
}

public class AgentMessageResponse
{
    public string AgentId { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}

public class AgentStatsResponse
{
    public string AgentId { get; set; } = string.Empty;
    public int ProcessedEventsCount { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

public class AgentEventRequest
{
    public string Message { get; set; } = string.Empty;
}

public class ComplexAgentCreatedResponse
{
    public string AgentId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool TestDataInitialized { get; set; }
}
