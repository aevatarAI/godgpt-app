using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Application.Grains.UserStatistics;
using Aevatar.Payment.Agents;
using Aevatar.Application.Grains.Twitter;
using Aevatar.Agents.GodGPT.Protos.Twitter;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserFeedback;
using Aevatar.Application.Grains.Agents.Anonymous;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.HttpApi.Host.Controllers;

/// <summary>
/// Test controller for State Migration - tests agent loading after migration
/// </summary>
[Route("api/admin/migration/test")]
[Authorize(Roles = "admin")]
public class StateMigrationTestController : AbpControllerBase
{
    private readonly IMongoClient _mongoClient;
    private readonly IConfiguration _configuration;
    private readonly IGAgentActorFactory _actorFactory;

    // Map agent type names to their state type names for collection lookup
    private static readonly Dictionary<string, string> AgentTypeToStateTypeMap = new()
    {
        { "UserStatisticsGAgent", "UserStatisticsState" },
        { "AnonymousUserGAgent", "AnonymousUserState" },
        { "PaymentIndexGAgent", "PaymentIndexStateProto" },
        { "TwitterAuthGAgent", "TwitterAuthState" },
        { "TwitterIdentityBindingGAgent", "TwitterIdentityBindingState" },
        { "UserQuotaGAgent", "UserQuotaState" },
        { "InvitationGAgent", "InvitationState" },
        { "UserFeedbackGAgent", "UserFeedbackState" },
        { "UserInfoCollectionGAgent", "UserInfoCollectionState" },
        { "InviteCodeGAgent", "InviteCodeState" },
        { "FreeTrialCodeFactoryGAgent", "FreeTrialCodeFactoryState" },
        { "ConfigurationGAgent", "ConfigurationState" },
        { "AwakeningGAgent", "AwakeningState" },
        { "GodChatGAgent", "GodChatStateProto" },
        { "ChatManagerGAgent", "ChatManagerStateProto" },
        { "AIAgentStatusProxy", "AIAgentStatusProxyState" },
        { "DailyContentGAgent", "DailyContentState" }
    };

    public StateMigrationTestController(
        IMongoClient mongoClient,
        IConfiguration configuration,
        IGAgentActorFactory actorFactory)
    {
        _mongoClient = mongoClient;
        _configuration = configuration;
        _actorFactory = actorFactory;
    }

    /// <summary>
    /// Test loading agents after migration - tests 5 agents per type via agent loading
    /// POST /api/admin/migration/test/load-agents
    /// </summary>
    [HttpPost("load-agents")]
    public async Task<IActionResult> TestLoadAsync([FromBody] TestLoadRequest? request = null)
    {
        var results = new List<LoadTestResult>();
        const int agentsPerType = 5;

        try
        {
            var databaseName = _configuration.GetSection("Storage")
                .GetValue<string>("DatabaseName") 
                ?? _configuration.GetConnectionString("Orleans")?.Split('/').LastOrDefault()?.Split('?').FirstOrDefault()
                ?? "AevatarBusiness";
            var database = _mongoClient.GetDatabase(databaseName);

            // Test UserStatisticsGAgent
            await TestAgentTypeAsync<UserStatisticsGAgent, IUserStatisticsGAgent>(
                database, "UserStatisticsGAgent", agentsPerType, results,
                async (agent) => await agent.GetUserStatisticsAsync()
            );

            // Test PaymentIndexGAgent
            await TestAgentTypeAsync<PaymentIndexGAgent, IPaymentIndexGAgent>(
                database, "PaymentIndexGAgent", agentsPerType, results,
                async (agent) => new
                {
                    ActiveSubscriptionsCount = (await agent.GetActiveSubscriptionsAsync()).Subscriptions.Count,
                    TotalPaymentCount = await agent.GetTotalPaymentCountAsync()
                }
            );


            // Test TwitterAuthGAgent
            await TestAgentTypeAsync<TwitterAuthGAgent, ITwitterAuthGAgent>(
                database, "TwitterAuthGAgent", agentsPerType, results,
                async (agent) => new
                {
                    BindStatus = await agent.GetBindStatusAsync()
                }
            );

            // Test TwitterIdentityBindingGAgent
            await TestAgentTypeAsync<TwitterIdentityBindingGAgent, ITwitterIdentityBindingGAgent>(
                database, "TwitterIdentityBindingGAgent", agentsPerType, results,
                async (agent) => await agent.GetBindStatusAsync()
            );

            // Test UserQuotaGAgent - use RPC interface
            await TestAgentTypeAsync<UserQuotaGAgent, IUserQuotaGAgent>(
                database, "UserQuotaGAgent", agentsPerType, results,
                async (agent) => new
                {
                    Credits = await agent.GetCreditsAsync(),
                    IsSubscribed = await agent.IsSubscribedAsync()
                }
            );

            // Test InvitationGAgent - use RPC interface
            await TestAgentTypeAsync<InvitationGAgent, IInvitationGAgent>(
                database, "InvitationGAgent", agentsPerType, results,
                async (agent) => await agent.GetInvitationStatsAsync()
            );

            // Test UserFeedbackGAgent - use RPC interface
            await TestAgentTypeAsync<UserFeedbackGAgent, IUserFeedbackGAgent>(
                database, "UserFeedbackGAgent", agentsPerType, results,
                async (agent) => await agent.CheckFeedbackEligibilityAsync()
            );

            // Test AnonymousUserGAgent - 53,369 records migrated
            await TestAgentTypeAsync<AnonymousUserGAgent, IAnonymousUserGAgent>(
                database, "AnonymousUserGAgent", agentsPerType, results,
                async (agent) => new
                {
                    ChatCount = await agent.GetChatCountAsync(),
                    CanChat = await agent.CanChatAsync(),
                    RemainingChats = await agent.GetRemainingChatsAsync(),
                    MaxChatCount = await agent.GetMaxChatCountAsync(),
                    CurrentSession = await agent.GetCurrentSessionAsync()
                }
            );

            // Test GodChatGAgent - 84,992 records migrated
            await TestAgentTypeAsync<GodChatGAgent, IGodChat>(
                database, "GodChatGAgent", agentsPerType, results,
                async (agent) => new
                {
                    ChatMessages = await agent.GetChatMessageAsync(),
                    ChatMessagesWithMeta = await agent.GetChatMessageWithMetaAsync()
                }
            );

            // Test ChatManagerGAgent (ChatGAgentManager) - ChatManagerStateProto migrated
            await TestAgentTypeAsync<ChatGAgentManager, IChatManagerGAgent>(
                database, "ChatManagerGAgent", agentsPerType, results,
                async (agent) => new
                {
                    SessionList = await agent.GetSessionListAsync()
                }
            );

            return Ok(new
            {
                Summary = new
                {
                    TotalTests = results.Count,
                    SuccessCount = results.Count(r => r.Success),
                    FailedCount = results.Count(r => !r.Success)
                },
                Results = results
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message, StackTrace = ex.StackTrace });
        }
    }

    private async Task TestAgentTypeAsync<TAgent, TInterface>(
        IMongoDatabase database,
        string agentTypeName,
        int maxAgents,
        List<LoadTestResult> results,
        Func<TInterface, Task<object>> getStateDataAsync)
        where TAgent : class, IGAgent
        where TInterface : class, IGAgent
    {
        try
        {
            if (!AgentTypeToStateTypeMap.TryGetValue(agentTypeName, out var stateTypeName))
            {
                results.Add(new LoadTestResult
                {
                    TestName = $"Test {agentTypeName}",
                    AgentId = "N/A",
                    Success = false,
                    Error = $"State type mapping not found for {agentTypeName}"
                });
                return;
            }

            var collectionName = $"agent_states_{stateTypeName}";
            var collection = database.GetCollection<BsonDocument>(collectionName);
            
            // Get up to maxAgents agent IDs from MongoDB
            var agentIds = await collection
                .Find(FilterDefinition<BsonDocument>.Empty)
                .Limit(maxAgents)
                .Project(Builders<BsonDocument>.Projection.Include("_id"))
                .ToListAsync();

            if (agentIds.Count == 0)
            {
                results.Add(new LoadTestResult
                {
                    TestName = $"Test {agentTypeName}",
                    AgentId = "N/A",
                    Success = false,
                    Error = $"No agents found in collection {collectionName}"
                });
                return;
            }

            // Test each agent
            foreach (var doc in agentIds)
            {
                if (!doc.Contains("_id"))
                    continue;

                var agentId = doc["_id"].AsString;
                
                try
                {
                    // Create agent actor and activate it
                    var actor = await _actorFactory.CreateGAgentActorAsync<TAgent>(agentId);
                    await actor.ActivateAsync();
                    
                    // Get agent interface proxy (works for both Local and Orleans runtime)
                    var agent = actor.As<TInterface>();
                    
                    // Get state data via agent interface
                    var stateData = await getStateDataAsync(agent);
                    
                    results.Add(new LoadTestResult
                    {
                        TestName = $"Load {agentTypeName}",
                        AgentId = agentId,
                        Success = true,
                        StateData = stateData
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new LoadTestResult
                    {
                        TestName = $"Load {agentTypeName}",
                        AgentId = agentId,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }
        }
        catch (Exception ex)
        {
            results.Add(new LoadTestResult
            {
                TestName = $"Test {agentTypeName}",
                AgentId = "N/A",
                Success = false,
                Error = ex.Message
            });
        }
    }

    private async Task TestAgentTypeWithDirectStateAsync<TAgent>(
        IMongoDatabase database,
        string agentTypeName,
        int maxAgents,
        List<LoadTestResult> results)
        where TAgent : class, IGAgent
    {
        try
        {
            if (!AgentTypeToStateTypeMap.TryGetValue(agentTypeName, out var stateTypeName))
            {
                results.Add(new LoadTestResult
                {
                    TestName = $"Test {agentTypeName}",
                    AgentId = "N/A",
                    Success = false,
                    Error = $"State type mapping not found for {agentTypeName}"
                });
                return;
            }

            var collectionName = $"agent_states_{stateTypeName}";
            var collection = database.GetCollection<BsonDocument>(collectionName);
            
            // Get up to maxAgents agent IDs from MongoDB
            var agentIds = await collection
                .Find(FilterDefinition<BsonDocument>.Empty)
                .Limit(maxAgents)
                .Project(Builders<BsonDocument>.Projection.Include("_id"))
                .ToListAsync();

            if (agentIds.Count == 0)
            {
                results.Add(new LoadTestResult
                {
                    TestName = $"Test {agentTypeName}",
                    AgentId = "N/A",
                    Success = false,
                    Error = $"No agents found in collection {collectionName}"
                });
                return;
            }

            // Test each agent
            foreach (var doc in agentIds)
            {
                if (!doc.Contains("_id"))
                    continue;

                var agentId = doc["_id"].AsString;
                
                try
                {
                    // Create agent actor and activate it
                    var actor = await _actorFactory.CreateGAgentActorAsync<TAgent>(agentId);
                    await actor.ActivateAsync();
                    
                    // Try to get agent instance (works only for Local runtime)
                    IGAgent? agentInstance = null;
                    try
                    {
                        agentInstance = actor.GetAgent() as TAgent;
                    }
                    catch (NotSupportedException)
                    {
                        // Orleans runtime - agent runs in Silo, cannot access directly
                        // For Orleans, we can only test via RPC interface methods
                    }
                    
                    // Access State property via reflection
                    var stateProperty = typeof(TAgent).GetProperty("State");
                    var state = stateProperty?.GetValue(agentInstance);
                    
                    results.Add(new LoadTestResult
                    {
                        TestName = $"Load {agentTypeName}",
                        AgentId = agentId,
                        Success = state != null,
                        StateData = state != null ? new { StateLoaded = true, StateType = state.GetType().Name } : null,
                        Error = state == null ? "Failed to access State property" : null
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new LoadTestResult
                    {
                        TestName = $"Load {agentTypeName}",
                        AgentId = agentId,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }
        }
        catch (Exception ex)
        {
            results.Add(new LoadTestResult
            {
                TestName = $"Test {agentTypeName}",
                AgentId = "N/A",
                Success = false,
                Error = ex.Message
            });
        }
    }
}

public class TestLoadRequest
{
    // Optional: can specify specific agent types to test
    public List<string>? AgentTypes { get; set; }
}

public class LoadTestResult
{
    public string TestName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? StateData { get; set; }
    public string? Error { get; set; }
}
