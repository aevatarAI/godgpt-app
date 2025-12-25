using System;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
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
        var rawId = Guid.NewGuid().ToString("D");
        
        _logger.LogInformation("🚀 Creating agent (rawId={RawId})", rawId);

        try
        {
            // Create and register agent actor (Agent is created in Silo/Grain)
            var actor = await _actorManager.CreateAndRegisterAsync<SimpleBusinessAgent>(rawId);

            // Get description via Actor proxy (calls Grain RPC)
            var description = await actor.GetDescriptionAsync();

            _logger.LogInformation("✅ Agent {ActorId} created successfully (running in Silo)", actor.Id);

            return Ok(new AgentCreatedResponse
            {
                // Return the normalized ActorId (Type:RawId) so clients can use it for follow-up calls.
                AgentId = actor.Id,
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating agent (rawId={RawId})", rawId);
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
        string actorId;
        try
        {
            // Accept both raw id and full actor id for convenience.
            actorId = AgentId.Normalize<SimpleBusinessAgent>(agentId);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid agent ID format. Use raw Guid or full ActorId (AgentType:Guid).");
        }

        _logger.LogInformation("📨 Sending message to agent {AgentId}: {Message}", 
            actorId, request.Message);

        try
        {
            // Get existing agent or create if not exists
            var actor = await _actorManager.GetActorAsync(actorId);
            if (actor == null)
            {
                actor = await _actorManager.CreateAndRegisterAsync<SimpleBusinessAgent>(actorId);
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
                AgentId = actorId,
                Response = description,
                ProcessedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing message for agent {AgentId}", actorId);
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
        string actorId;
        try
        {
            actorId = AgentId.Normalize<SimpleBusinessAgent>(agentId);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid agent ID format. Use raw Guid or full ActorId (AgentType:Guid).");
        }

        _logger.LogInformation("📊 Getting stats for agent {AgentId}", actorId);

        try
        {
            // Get existing agent
            var actor = await _actorManager.GetActorAsync(actorId);
            if (actor == null)
            {
                return NotFound($"Agent {actorId} not found");
            }

            // Get description via Grain RPC (contains stats info)
            var description = await actor.GetDescriptionAsync();

            return Ok(new AgentStatsResponse
            {
                AgentId = actorId,
                ProcessedEventsCount = 0, // Stats are embedded in description
                LastMessage = description,
                LastUpdated = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting stats for agent {AgentId}", actorId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Set parent for agent (establishes hierarchy)
    /// </summary>
    [HttpPost("agents/{childId}/parent/{parentId}")]
    public async Task<IActionResult> SetParent([FromRoute] string childId, [FromRoute] string parentId)
    {
        string childActorId;
        string parentActorId;
        try
        {
            // Demo endpoint: treat both parent/child as SimpleBusinessAgent
            childActorId = AgentId.Normalize<SimpleBusinessAgent>(childId);
            parentActorId = AgentId.Normalize<SimpleBusinessAgent>(parentId);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid ID format. Use raw Guid or full ActorId (AgentType:Guid).");
        }

        try
        {
            var childActor = await _actorManager.GetActorAsync(childActorId);
            if (childActor == null) return NotFound($"Child agent {childActorId} not found");

            var parentActor = await _actorManager.GetActorAsync(parentActorId);
            if (parentActor == null) return NotFound($"Parent agent {parentActorId} not found");

            // Establish bidirectional relationship using ActorHierarchyCoordinator
            await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger);
            
            _logger.LogInformation("✅ Parent-child relationship established: {Child} -> {Parent}",
                childActorId, parentActorId);
            return Ok(new { message = $"Child {childActorId} now has parent {parentActorId}" });
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
        string actorId;
        try
        {
            actorId = AgentId.Normalize<SimpleBusinessAgent>(agentId);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid agent ID format. Use raw Guid or full ActorId (AgentType:Guid).");
        }

        try
        {
            var actor = await _actorManager.GetActorAsync(actorId);
            if (actor == null) return NotFound($"Agent {actorId} not found");

            // Create event and publish via Actor proxy (processed in Silo/Grain)
            var evt = new Business.Server.BusinessMessageEvent
            {
                Message = request.Message,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await actor.PublishEventAsync(evt, EventDirection.Down);
            
            _logger.LogInformation("✅ Event published to agent {AgentId} (processed in Silo)", actorId);
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
        var rawId = Guid.NewGuid().ToString("D");
        
        _logger.LogInformation("🧪 Creating ComplexStateAgent (rawId={RawId})", rawId);

        try
        {
            // Create and register complex state agent
            var actor = await _actorManager.CreateAndRegisterAsync<ComplexStateAgent>(rawId);

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
                _logger.LogWarning(
                    "Could not get agent instance directly (Orleans mode). " +
                    "Use /complex-agent/{AgentId}/init to initialize test data.",
                    actor.Id);
            }

            var description = await actor.GetDescriptionAsync();

            _logger.LogInformation("✅ ComplexStateAgent {AgentId} created with test data", actor.Id);

            return Ok(new ComplexAgentCreatedResponse
            {
                AgentId = actor.Id,
                Description = description,
                AgentType = "Aevatar.App.Agents.Agents.ComplexStateAgent",
                CreatedAt = DateTime.UtcNow,
                TestDataInitialized = agent != null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating ComplexStateAgent (rawId={RawId})", rawId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Initialize test data for existing complex agent (for Orleans mode).
    /// </summary>
    [HttpPost("complex-agent/{agentId}/init")]
    public async Task<IActionResult> InitComplexAgentTestData([FromRoute] string agentId)
    {
        string actorId;
        try
        {
            actorId = AgentId.Normalize<ComplexStateAgent>(agentId);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid agent ID format. Use raw Guid or full ActorId (AgentType:Guid).");
        }

        try
        {
            var actor = await _actorManager.GetActorAsync(actorId);
            if (actor == null)
            {
                return NotFound($"Agent {actorId} not found");
            }

            var agent = actor.GetAgent() as ComplexStateAgent;
            if (agent != null)
            {
                await agent.InitializeTestDataAsync();
                return Ok(new { message = "Test data initialized", agentId = actorId });
            }
            else
            {
                return BadRequest("Agent is not a ComplexStateAgent or not accessible (Orleans mode)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error initializing test data for agent {AgentId}", actorId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get complex agent state (for verification).
    /// </summary>
    [HttpGet("complex-agent/{agentId}/state")]
    public async Task<ActionResult<object>> GetComplexAgentState([FromRoute] string agentId)
    {
        string actorId;
        try
        {
            actorId = AgentId.Normalize<ComplexStateAgent>(agentId);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid agent ID format. Use raw Guid or full ActorId (AgentType:Guid).");
        }

        try
        {
            var actor = await _actorManager.GetActorAsync(actorId);
            if (actor == null)
            {
                return NotFound($"Agent {actorId} not found");
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
            _logger.LogError(ex, "❌ Error getting state for agent {AgentId}", actorId);
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
