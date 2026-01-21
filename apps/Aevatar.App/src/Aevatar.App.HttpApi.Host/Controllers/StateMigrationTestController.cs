using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.App.HttpApi.Host.BackgroundJobs;
using Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.Agents.GodGPT.Protos.GoogleAuth;
using Aevatar.Application.Grains.UserStatistics;
using Aevatar.Payment.Agents;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.HttpApi.Host.Controllers;

/// <summary>
/// Test controller for State Migration with mock data
/// </summary>
[RemoteService]
[Route("api/admin/migration/test")]
// [Authorize] // Temporarily disabled for testing
public class StateMigrationTestController : AbpControllerBase
{
    private readonly IMongoClient _mongoClient;
    private readonly IConfiguration _configuration;
    private readonly IGAgentActorFactory _actorFactory;

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
    /// Import mock data from JSON file and test write/read
    /// POST /api/admin/migration/test/import-mock
    /// </summary>
    [HttpPost("import-mock")]
    public async Task<IActionResult> ImportMockDataAsync([FromBody] ImportMockRequest request)
    {
        var results = new List<ImportResult>();

        try
        {
            // Read JSON file
            var jsonPath = request.JsonFilePath;
            if (string.IsNullOrEmpty(jsonPath))
            {
                // Default path: from project root
                jsonPath = "/Users/liyingpei/Desktop/Code/godgpt-app/src/mock_state_export.json";
            }
            
            if (!Path.IsPathRooted(jsonPath))
            {
                // Try multiple possible base paths
                var possiblePaths = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), jsonPath),
                    Path.Combine(AppContext.BaseDirectory, jsonPath),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "mock_state_export.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "mock_state_export.json"),
                    "/Users/liyingpei/Desktop/Code/godgpt-app/src/mock_state_export.json"
                };

                jsonPath = possiblePaths.FirstOrDefault(System.IO.File.Exists) ?? jsonPath;
            }

            if (!System.IO.File.Exists(jsonPath))
            {
                return BadRequest(new { Error = $"File not found: {jsonPath}" });
            }

            var jsonContent = await System.IO.File.ReadAllTextAsync(jsonPath);
            var mockRecords = JsonSerializer.Deserialize<List<MockRecord>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (mockRecords == null || mockRecords.Count == 0)
            {
                return BadRequest(new { Error = "No records found in JSON file" });
            }

            // Get database
            var databaseName = _configuration.GetSection("Storage")
                .GetValue<string>("DatabaseName")
                ?? _configuration.GetConnectionString("Orleans")?.Split('/').LastOrDefault()?.Split('?').FirstOrDefault()
                ?? "AevatarBusiness";

            var database = _mongoClient.GetDatabase(databaseName);

            // Process each record
            foreach (var record in mockRecords)
            {
                var result = new ImportResult
                {
                    Id = record.Id,
                    Type = ExtractTypeName(record.Id)
                };

                try
                {
                    // Convert Agent ID format
                    var newAgentId = ConvertAgentId(record.Id, result.Type);

                    // Get converter using the same logic as StateMigrationJob
                    var converter = GetConverter(result.Type);
                    if (converter != null)
                    {
                        // Convert State using the converter
                        var newState = converter.Convert(record.State);
                        if (newState != null)
                        {
                            // Write to MongoDB
                            var writeSuccess = await WriteStateAsync(database, newAgentId, newState, result.Type);
                            result.WriteSuccess = writeSuccess;

                            if (writeSuccess)
                            {
                                // Read back to verify (simplified - just check if we can read it)
                                var readState = await ReadStateAsync(database, newAgentId, result.Type, newState.GetType());
                                result.ReadSuccess = readState != null;
                                result.Verified = readState != null; // Simplified verification
                            }
                        }
                        else
                        {
                            result.Error = "Failed to convert state (converter returned null)";
                        }
                    }
                    else
                    {
                        result.Error = $"Converter not implemented for type: {result.Type}";
                    }
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }

                results.Add(result);
            }

            var summary = new
            {
                Total = results.Count,
                WriteSuccess = results.Count(r => r.WriteSuccess),
                ReadSuccess = results.Count(r => r.ReadSuccess),
                Verified = results.Count(r => r.Verified),
                Failed = results.Count(r => !string.IsNullOrEmpty(r.Error)),
                Results = results
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message, StackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Test loading agent state using Agent methods directly
    /// POST /api/admin/migration/test/test-load
    /// </summary>
    [HttpPost("test-load")]
    public async Task<IActionResult> TestLoadAsync([FromBody] TestLoadRequest request)
    {
        var results = new List<LoadTestResult>();

        try
        {
            // Test 1: Load using deviceId format agentId (RecordAppRatingAsync scenario)
            var deviceIdAgentId = request.DeviceIdAgentId ?? "UserStatisticsGAgent:2e08d2bc65ad4c579afee776ba45d0d6";
            
            try
            {
                var actor = await _actorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(deviceIdAgentId);
                var agent = actor.As<IUserStatisticsGAgent>();
                
                // Try to get user statistics
                var statistics = await agent.GetUserStatisticsAsync();
                
                // Also test direct State access to verify all fields
                // Get Agent instance and access State directly
                var agentInstance = actor.GetAgent() as UserStatisticsGAgent;
                var state = agentInstance?.GetState();
                
                results.Add(new LoadTestResult
                {
                    TestName = "Load Agent with deviceId agentId",
                    AgentId = deviceIdAgentId,
                    Success = true,
                    StateData = new
                    {
                        // From GetUserStatisticsAsync response (RPC method)
                        RpcMethodResult = new
                        {
                            UserId = statistics.UserId,
                            AppRatingsCount = statistics.AppRatings.Count,
                            AppRatings = statistics.AppRatings.Select(r => new
                            {
                                Platform = r.Platform,
                                DeviceId = r.DeviceId,
                                FirstRatingTime = r.FirstRatingTime?.ToDateTime().ToString("O"),
                                LastRatingTime = r.LastRatingTime?.ToDateTime().ToString("O"),
                                RatingCount = r.RatingCount
                            }).ToList()
                        },
                        // Direct State access to verify all fields
                        DirectStateAccess = state != null ? new
                        {
                            UserId = state.UserId,
                            IsInitialized = state.IsInitialized,
                            IsRealUser = state.IsRealUser,
                            AppRatingsMapCount = state.AppRatings.Count,
                            AppRatingsMap = state.AppRatings.ToDictionary(
                                kvp => kvp.Key,
                                kvp => new
                                {
                                    Platform = kvp.Value.Platform,
                                    DeviceId = kvp.Value.DeviceId,
                                    FirstRatingTime = kvp.Value.FirstRatingTime?.ToDateTime().ToString("O"),
                                    LastRatingTime = kvp.Value.LastRatingTime?.ToDateTime().ToString("O"),
                                    RatingCount = kvp.Value.RatingCount
                                })
                        } : null
                    }
                });
            }
            catch (Exception ex)
            {
                results.Add(new LoadTestResult
                {
                    TestName = "Load Agent with deviceId agentId",
                    AgentId = deviceIdAgentId,
                    Success = false,
                    Error = ex.Message
                });
            }

            // Test 2: Load using userId format agentId (GetUserStatisticsAsync scenario)
            // First get userId from deviceId state
            try
            {
                var actor1 = await _actorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(deviceIdAgentId);
                var agent1 = actor1.As<IUserStatisticsGAgent>();
                var stats1 = await agent1.GetUserStatisticsAsync();
                
                if (!string.IsNullOrEmpty(stats1.UserId))
                {
                    var userIdAgentId = $"UserStatisticsGAgent:{stats1.UserId}";
                    
                    try
                    {
                        var actor2 = await _actorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(userIdAgentId);
                        var agent2 = actor2.As<IUserStatisticsGAgent>();
                        var statistics2 = await agent2.GetUserStatisticsAsync();
                        
                        results.Add(new LoadTestResult
                        {
                            TestName = "Load Agent with userId agentId",
                            AgentId = userIdAgentId,
                            Success = true,
                            StateData = new
                            {
                                UserId = statistics2.UserId,
                                AppRatingsCount = statistics2.AppRatings.Count,
                                AppRatings = statistics2.AppRatings.Select(r => new
                                {
                                    Platform = r.Platform,
                                    DeviceId = r.DeviceId,
                                    RatingCount = r.RatingCount
                                }).ToList()
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new LoadTestResult
                        {
                            TestName = "Load Agent with userId agentId",
                            AgentId = userIdAgentId,
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
                    TestName = "Get userId from deviceId agent",
                    AgentId = deviceIdAgentId,
                    Success = false,
                    Error = ex.Message
                });
            }

            // Test 3: PaymentIndexGAgent (migrated from UserBillingGAgent)
            try
            {
                // Try to find a migrated PaymentIndexGAgent from MongoDB
                var databaseName = _configuration.GetSection("Storage")
                    .GetValue<string>("DatabaseName") 
                    ?? _configuration.GetConnectionString("Orleans")?.Split('/').LastOrDefault()?.Split('?').FirstOrDefault()
                    ?? "AevatarBusiness";
                var database = _mongoClient.GetDatabase(databaseName);
                var paymentIndexCollection = database.GetCollection<BsonDocument>("agent_states_PaymentIndexStateProto");
                var paymentIndexDoc = await paymentIndexCollection.Find(FilterDefinition<BsonDocument>.Empty).FirstOrDefaultAsync();
                
                if (paymentIndexDoc == null)
                {
                    // Try alternative collection name (if old migration used UserBillingGAgent)
                    var userBillingCollection = database.GetCollection<BsonDocument>("agent_states_UserBillingStateProto");
                    paymentIndexDoc = await userBillingCollection.Find(FilterDefinition<BsonDocument>.Empty).FirstOrDefaultAsync();
                }
                
                if (paymentIndexDoc == null)
                {
                    results.Add(new LoadTestResult
                    {
                        TestName = "Find PaymentIndexGAgent in MongoDB",
                        AgentId = "N/A",
                        Success = false,
                        Error = "No PaymentIndexGAgent records found in MongoDB. Collection may be empty or use different name."
                    });
                }
                else if (paymentIndexDoc != null && paymentIndexDoc.Contains("_id"))
                {
                    var paymentAgentId = paymentIndexDoc["_id"].AsString;
                    
                    try
                    {
                        var paymentActor = await _actorFactory.CreateGAgentActorAsync<Aevatar.Payment.Agents.PaymentIndexGAgent>(paymentAgentId);
                        var paymentAgent = paymentActor.As<Aevatar.Payment.Agents.IPaymentIndexGAgent>();
                        
                        // Test RPC methods
                        var activeSubscriptions = await paymentAgent.GetActiveSubscriptionsAsync();
                        var totalPaymentCount = await paymentAgent.GetTotalPaymentCountAsync();
                        var stripeCustomerId = await paymentAgent.GetPlatformCustomerIdAsync(PaymentPlatform.Stripe);
                        
                        results.Add(new LoadTestResult
                        {
                            TestName = "Load PaymentIndexGAgent (migrated from UserBillingGAgent)",
                            AgentId = paymentAgentId,
                            Success = true,
                            StateData = new
                            {
                                ActiveSubscriptionsCount = activeSubscriptions.Subscriptions.Count,
                                TotalPaymentCount = totalPaymentCount,
                                StripeCustomerId = stripeCustomerId,
                                ActiveSubscriptions = activeSubscriptions.Subscriptions.Select(s => new
                                {
                                    PaymentId = s.PaymentId,
                                    BusinessType = s.BusinessType,
                                    BusinessId = s.BusinessId,
                                    Platform = s.Platform,
                                    ProductName = s.ProductName,
                                    Amount = s.Amount,
                                    Currency = s.Currency,
                                    PeriodEnd = s.PeriodEnd?.ToDateTime().ToString("O")
                                }).ToList()
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new LoadTestResult
                        {
                            TestName = "Load PaymentIndexGAgent",
                            AgentId = paymentAgentId ?? "unknown",
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
                    TestName = "Find PaymentIndexGAgent in MongoDB",
                    AgentId = "N/A",
                    Success = false,
                    Error = ex.Message
                });
            }

            // Test 4: GoogleAuthGAgent
            try
            {
                var databaseName = _configuration.GetSection("Storage")
                    .GetValue<string>("DatabaseName") 
                    ?? _configuration.GetConnectionString("Orleans")?.Split('/').LastOrDefault()?.Split('?').FirstOrDefault()
                    ?? "AevatarBusiness";
                var database = _mongoClient.GetDatabase(databaseName);
                var googleAuthCollection = database.GetCollection<BsonDocument>("agent_states_GoogleAuthStateProto");
                var googleAuthDoc = await googleAuthCollection.Find(FilterDefinition<BsonDocument>.Empty).FirstOrDefaultAsync();
                
                if (googleAuthDoc == null)
                {
                    results.Add(new LoadTestResult
                    {
                        TestName = "Find GoogleAuthGAgent in MongoDB",
                        AgentId = "N/A",
                        Success = false,
                        Error = "No GoogleAuthGAgent records found in MongoDB. Collection may be empty or use different name."
                    });
                }
                else if (googleAuthDoc != null && googleAuthDoc.Contains("_id"))
                {
                    var googleAuthAgentId = googleAuthDoc["_id"].AsString;
                    
                    // Read state directly from MongoDB (GoogleAuthGAgent may not have RPC interface)
                    var stateData = googleAuthDoc["StateData"].AsBsonBinaryData.Bytes;
                    var state = Aevatar.Agents.GodGPT.Protos.GoogleAuth.GoogleAuthStateProto.Parser.ParseFrom(stateData);
                    
                    results.Add(new LoadTestResult
                    {
                        TestName = "Load GoogleAuthGAgent state from MongoDB",
                        AgentId = googleAuthAgentId,
                        Success = true,
                        StateData = new
                        {
                            UserId = state.UserId,
                            GoogleId = state.GoogleId,
                            Email = state.Email,
                            DisplayName = state.DisplayName,
                            HasAccessToken = !string.IsNullOrEmpty(state.AccessToken),
                            HasRefreshToken = !string.IsNullOrEmpty(state.RefreshToken),
                            TokenExpiresAt = state.TokenExpiresAt?.ToDateTime().ToString("O"),
                            LastLoginAt = state.LastLoginAt?.ToDateTime().ToString("O")
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new LoadTestResult
                {
                    TestName = "Load GoogleAuthGAgent",
                    AgentId = "N/A",
                    Success = false,
                    Error = ex.Message
                });
            }

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

    /// <summary>
    /// Read a state from MongoDB to verify
    /// GET /api/admin/migration/test/read/{agentId}
    /// </summary>
    [HttpGet("read/{agentId}")]
    public async Task<IActionResult> ReadStateAsync(string agentId)
    {
        try
        {
            var databaseName = _configuration.GetSection("Storage")
                .GetValue<string>("DatabaseName")
                ?? _configuration.GetConnectionString("Orleans")?.Split('/').LastOrDefault()?.Split('?').FirstOrDefault()
                ?? "AevatarBusiness";

            var database = _mongoClient.GetDatabase(databaseName);
            
            // Extract type name from agentId (format: "TypeName:guid" or "FullTypeName/guid")
            string typeName;
            if (agentId.Contains(':'))
            {
                // New format: "UserStatisticsGAgent:guid"
                typeName = agentId.Split(':')[0];
            }
            else
            {
                // Old format: "FullTypeName/guid"
                typeName = ExtractTypeName(agentId);
            }

            // For ReadStateAsync endpoint, we need to determine the state type from the agent type
            // This is a simplified version - in production, we'd need to map agent type to state type
            System.Type? stateType = null;
            if (typeName.Contains("UserStatistics", StringComparison.OrdinalIgnoreCase))
            {
                stateType = typeof(UserStatisticsState);
            }
            // Add more mappings as needed for other agent types
            
            if (stateType == null)
            {
                return BadRequest(new { Error = $"Cannot determine state type for agent type: {typeName}" });
            }
            
            var state = await ReadStateAsync(database, agentId, typeName, stateType);
            
            if (state == null)
            {
                return NotFound(new { Error = $"State not found for AgentId: {agentId}, Type: {typeName}, Collection: Stream{typeName}" });
            }

            // Convert Protobuf to JSON for display
            var json = JsonFormatter.Default.Format(state);
            return Ok(new { AgentId = agentId, Type = typeName, State = json });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message, StackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Get converter for a specific type (same logic as StateMigrationJob)
    /// </summary>
    private IStateConverter? GetConverter(string typeName)
    {
        // ExtractTypeName already returns short name (e.g., "AnonymousUserGAgent")
        // So we can use it directly, but handle special cases
        var shortName = typeName;
        
        // Handle special case: ChatGAgentManager -> ChatManagerGAgent
        if (shortName == "ChatGAgentManager")
            shortName = "ChatManagerGAgent";
        
        // Debug: log the conversion attempt
        System.Console.WriteLine($"[StateMigrationTest] GetConverter: typeName='{typeName}', shortName='{shortName}'");
        
            IStateConverter? converter = shortName switch
            {
                "UserStatisticsGAgent" => new UserStatisticsStateConverter(),
                "AnonymousUserGAgent" => new AnonymousUserStateConverter(),
                "InvitationGAgent" => new InvitationStateConverter(),
                "UserQuotaGAgent" => new UserQuotaStateConverter(),
                "ChatManagerGAgent" => new ChatManagerStateConverter(),
                "GodChatGAgent" => new GodChatStateConverter(),
                "AwakeningGAgent" => new AwakeningStateConverter(),
                "ConfigurationGAgent" => new ConfigurationStateConverter(),
                "DailyContentGAgent" => new DailyContentStateConverter(),
                "FreeTrialCodeFactoryGAgent" => new FreeTrialCodeFactoryStateConverter(),
                "InviteCodeGAgent" => new InviteCodeStateConverter(),
                "UserFeedbackGAgent" => new UserFeedbackStateConverter(),
                "UserInfoCollectionGAgent" => new UserInfoCollectionStateConverter(),
                "LumenUserProfileGAgent" => new LumenUserProfileStateConverter(),
                "LumenPredictionGAgent" => new LumenPredictionStateConverter(),
                "LumenDailyYearlyHistoryGAgent" => new LumenDailyYearlyHistoryStateConverter(),
                "LumenFeedbackGAgent" => new LumenFeedbackStateConverter(),
                "UserBillingGAgent" => new UserBillingStateConverter(),
                "GoogleAuthGAgent" => new GoogleAuthStateConverter(),
                "AIAgentStatusProxy" => new AIAgentStatusProxyStateConverter(),
                _ => (IStateConverter?)null
            };
        
        System.Console.WriteLine($"[StateMigrationTest] GetConverter result: {(converter != null ? converter.GetType().Name : "null")}");
        
        return converter;
    }

    private string ExtractTypeName(string agentId)
    {
        // Extract type from ID: "Aevatar.Application.Grains.UserStatistics.UserStatisticsGAgent/guid"
        var parts = agentId.Split('/');
        if (parts.Length == 2)
        {
            var fullType = parts[0];
            var lastDot = fullType.LastIndexOf('.');
            return lastDot >= 0 ? fullType[(lastDot + 1)..] : fullType;
        }
        return "Unknown";
    }

    private string ConvertAgentId(string oldId, string typeName)
    {
        if (string.IsNullOrWhiteSpace(oldId))
            return oldId;

        if (oldId.Contains(':'))
            return oldId;

        var parts = oldId.Split('/');
        if (parts.Length != 2)
            return oldId;

        var guid = parts[1];
        var shortName = ExtractShortTypeName(typeName);
        return $"{shortName}:{guid}";
    }

    private string ExtractShortTypeName(string fullTypeName)
    {
        // If already short name (no dots), return as is
        if (!fullTypeName.Contains('.'))
            return fullTypeName;
        
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot >= 0 ? fullTypeName[(lastDot + 1)..] : fullTypeName;
    }

    private UserStatisticsState? ConvertUserStatisticsState(Dictionary<string, object>? oldState)
    {
        if (oldState == null)
            return new UserStatisticsState();

        var newState = new UserStatisticsState();

        // UserId
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = userIdObj?.ToString();
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
            {
                newState.UserId = guid.ToString("D");
            }
        }

        // IsInitialized
        if (oldState.TryGetValue("IsInitialized", out var isInitObj))
        {
            newState.IsInitialized = ConvertToBool(isInitObj);
        }

        // IsRealUser
        if (oldState.TryGetValue("IsRealUser", out var isRealUserObj))
        {
            newState.IsRealUser = ConvertToBool(isRealUserObj);
        }

        // AppRatings - Handle both array and dictionary formats
        if (oldState.TryGetValue("AppRatings", out var appRatingsObj) && appRatingsObj != null)
        {
            // Try array format first (from mock data)
            if (appRatingsObj is JsonElement jeArray && jeArray.ValueKind == JsonValueKind.Array)
            {
                // Mock data has array format: [{Rating, Comment, CreatedAt}, ...]
                // Convert to map format: {deviceId: {Platform, DeviceId, FirstRatingTime, ...}}
                int index = 0;
                foreach (var item in jeArray.EnumerateArray())
                {
                    var deviceId = $"device_{index}"; // Generate device ID
                    var ratingInfo = ConvertAppRatingInfoFromMock(item);
                    if (ratingInfo != null)
                    {
                        newState.AppRatings[deviceId] = ratingInfo;
                    }
                    index++;
                }
            }
            // Try dictionary format (normal format)
            else if (appRatingsObj is Dictionary<string, object> appRatingsDict)
            {
                foreach (var kvp in appRatingsDict)
                {
                    var deviceId = kvp.Key;
                    var ratingInfo = ConvertAppRatingInfo(kvp.Value);
                    if (ratingInfo != null)
                    {
                        newState.AppRatings[deviceId] = ratingInfo;
                    }
                }
            }
            else if (appRatingsObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        var deviceId = kvp.Key;
                        var ratingInfo = ConvertAppRatingInfo(kvp.Value);
                        if (ratingInfo != null)
                        {
                            newState.AppRatings[deviceId] = ratingInfo;
                        }
                    }
                }
            }
        }

        return newState;
    }

    /// <summary>
    /// Convert AppRatingInfo from mock data format (array item with Rating, Comment, CreatedAt)
    /// </summary>
    private AppRatingInfo? ConvertAppRatingInfoFromMock(JsonElement item)
    {
        var ratingInfo = new AppRatingInfo();

        // Platform - default to "Unknown" for mock data
        ratingInfo.Platform = "Unknown";

        // DeviceId - generate from index or use a default
        ratingInfo.DeviceId = $"mock_device_{Guid.NewGuid().ToString("N")[..8]}";

        // CreatedAt -> FirstRatingTime and LastRatingTime
        if (item.TryGetProperty("CreatedAt", out var createdAtProp))
        {
            var createdAtStr = createdAtProp.GetString();
            if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var dt))
            {
                ratingInfo.FirstRatingTime = Timestamp.FromDateTime(dt.ToUniversalTime());
                ratingInfo.LastRatingTime = Timestamp.FromDateTime(dt.ToUniversalTime());
            }
        }

        // Rating -> RatingCount (treat as count of 1 rating)
        if (item.TryGetProperty("Rating", out var ratingProp))
        {
            ratingInfo.RatingCount = 1; // Mock data has single rating, count = 1
        }

        return ratingInfo;
    }

    private AppRatingInfo? ConvertAppRatingInfo(object? ratingObj)
    {
        if (ratingObj == null)
            return null;

        var ratingInfo = new AppRatingInfo();
        Dictionary<string, object>? ratingDict = null;

        if (ratingObj is Dictionary<string, object> dict)
        {
            ratingDict = dict;
        }
        else if (ratingObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            ratingDict = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
        }

        if (ratingDict == null)
            return null;

        // Platform
        if (ratingDict.TryGetValue("Platform", out var platformObj))
        {
            ratingInfo.Platform = platformObj?.ToString() ?? string.Empty;
        }

        // DeviceId
        if (ratingDict.TryGetValue("DeviceId", out var deviceIdObj))
        {
            ratingInfo.DeviceId = deviceIdObj?.ToString() ?? string.Empty;
        }

        // FirstRatingTime
        if (ratingDict.TryGetValue("FirstRatingTime", out var firstTimeObj))
        {
            var dt = ConvertToDateTime(firstTimeObj);
            if (dt.HasValue)
            {
                ratingInfo.FirstRatingTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
        }

        // LastRatingTime
        if (ratingDict.TryGetValue("LastRatingTime", out var lastTimeObj))
        {
            var dt = ConvertToDateTime(lastTimeObj);
            if (dt.HasValue)
            {
                ratingInfo.LastRatingTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
        }

        // RatingCount
        if (ratingDict.TryGetValue("RatingCount", out var countObj))
        {
            ratingInfo.RatingCount = ConvertToInt32(countObj);
        }

        return ratingInfo;
    }

    private bool ConvertToBool(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (obj is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        if (bool.TryParse(obj.ToString(), out var result)) return result;
        return false;
    }

    private int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number)
            return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }

    private DateTime? ConvertToDateTime(object? obj)
    {
        if (obj == null) return null;
        if (obj is DateTime dt) return dt;
        if (obj is DateTimeOffset dto) return dto.DateTime;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(je.GetString(), out var dt2)) return dt2;
        }
        if (DateTime.TryParse(obj.ToString(), out var dt3)) return dt3;
        return null;
    }

    private async Task<bool> WriteStateAsync(IMongoDatabase database, string agentId, IMessage state, string agentTypeName)
    {
        try
        {
            // Use the same collection naming convention as MongoDBStateStore: agent_states_{StateTypeName}
            var stateTypeName = state.GetType().Name; // "UserStatisticsState"
            var collectionName = $"agent_states_{stateTypeName}";
            var collection = database.GetCollection<BsonDocument>(collectionName);

            var stateBytes = state.ToByteArray();
            var binaryData = new BsonBinaryData(stateBytes, BsonBinarySubType.Binary);

            // Use the same document structure as AgentStateDocument
            var document = new BsonDocument
            {
                { "AgentId", agentId },
                { "StateData", binaryData },
                { "StateType", stateTypeName },
                { "Version", 1L },
                { "UpdatedAt", DateTime.UtcNow }
            };

            var filter = Builders<BsonDocument>.Filter.Eq("AgentId", agentId);
            var options = new ReplaceOptions { IsUpsert = true };

            await collection.ReplaceOneAsync(filter, document, options);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Write failed: {ex.Message}");
            return false;
        }
    }

    private async Task<IMessage?> ReadStateAsync(IMongoDatabase database, string agentId, string agentTypeName, System.Type stateType)
    {
        try
        {
            // Use the same collection naming convention as MongoDBStateStore: agent_states_{StateTypeName}
            var stateTypeName = stateType.Name; // e.g., "UserStatisticsState", "AnonymousUserState", etc.
            var collectionName = $"agent_states_{stateTypeName}";
            var collection = database.GetCollection<BsonDocument>(collectionName);

            // Use _id field (matching migration format, AgentStateDocument maps AgentId to _id via [BsonId])
            var filter = Builders<BsonDocument>.Filter.Eq("_id", agentId);
            var doc = await collection.Find(filter).FirstOrDefaultAsync();

            if (doc == null)
                return null;

            // Use the same document structure as AgentStateDocument
            if (doc.Contains("StateData") && doc["StateData"].IsBsonBinaryData)
            {
                var binaryData = doc["StateData"].AsBsonBinaryData;
                // Use reflection to call ParseFrom on the specific state type
                var parserProperty = stateType.GetProperty("Parser", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (parserProperty != null)
                {
                    var parser = parserProperty.GetValue(null);
                    var parseFromMethod = parser?.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
                    if (parseFromMethod != null)
                    {
                        var state = parseFromMethod.Invoke(parser, new object[] { binaryData.Bytes }) as IMessage;
                        return state;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Read failed: {ex.Message}");
            return null;
        }
    }

    private bool VerifyState(UserStatisticsState? written, UserStatisticsState? read)
    {
        if (written == null || read == null)
            return false;

        return written.UserId == read.UserId &&
               written.IsInitialized == read.IsInitialized &&
               written.IsRealUser == read.IsRealUser &&
               written.AppRatings.Count == read.AppRatings.Count;
    }
}

public class ImportMockRequest
{
    public string? JsonFilePath { get; set; }
}

public class MockRecord
{
    public string Id { get; set; } = string.Empty;
    public string? ETag { get; set; }
    public Dictionary<string, object>? State { get; set; }
}

public class TestLoadRequest
{
    public string? DeviceIdAgentId { get; set; }
}

public class LoadTestResult
{
    public string TestName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? StateData { get; set; }
    public string? Error { get; set; }
}

public class ImportResult
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool WriteSuccess { get; set; }
    public bool ReadSuccess { get; set; }
    public bool Verified { get; set; }
    public string? Error { get; set; }
}
