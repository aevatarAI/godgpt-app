using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.Agents.GodGPT.Protos.Anonymous;
using Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs;

/// <summary>
/// Background job for migrating State data from old system to new system
/// Can be triggered manually via API or scheduled as recurring job
/// </summary>
public class StateMigrationJob
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpClient _httpClient;
    private readonly IMongoClient _mongoClient;
    private readonly ILogger<StateMigrationJob> _logger;
    private readonly StateMigrationOptions _options;
    private readonly string _databaseName;
    private string? _cachedToken;

    public StateMigrationJob(
        IHttpClientFactory httpClientFactory,
        IMongoClient mongoClient,
        ILogger<StateMigrationJob> logger,
        IOptions<StateMigrationOptions> options,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _httpClient = httpClientFactory.CreateClient();
        _mongoClient = mongoClient;
        _logger = logger;
        _options = options.Value;
        
        // Get database name from configuration
        _databaseName = configuration.GetSection("Storage")
            .GetValue<string>("DatabaseName") 
            ?? configuration.GetConnectionString("Orleans")?.Split('/').LastOrDefault()?.Split('?').FirstOrDefault()
            ?? "AevatarBusiness";
    }

    /// <summary>
    /// Ensure HTTP client has valid authorization token
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        // If token is already set and cached, use it
        if (!string.IsNullOrEmpty(_cachedToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _cachedToken);
            return;
        }

        // If token is provided in config, use it
        if (!string.IsNullOrEmpty(_options.OldSystemApiToken))
        {
            _cachedToken = _options.OldSystemApiToken;
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _cachedToken);
            return;
        }

        // Auto-fetch token if credentials are provided
        if (!string.IsNullOrEmpty(_options.TokenEndpoint) &&
            !string.IsNullOrEmpty(_options.Username) &&
            !string.IsNullOrEmpty(_options.Password))
        {
            try
            {
                _logger.LogInformation("[StateMigration] Fetching authentication token from {TokenEndpoint}", 
                    _options.TokenEndpoint);

                // Use a separate HttpClient for token request (no auth header)
                var tokenClient = _httpClientFactory.CreateClient();
                
                var tokenRequest = new Dictionary<string, string>
                {
                    { "grant_type", "password" },
                    { "username", _options.Username },
                    { "password", _options.Password },
                    { "client_id", _options.ClientId },
                    { "scope", _options.Scope }
                };

                var requestContent = new FormUrlEncodedContent(tokenRequest);
                var response = await tokenClient.PostAsync(_options.TokenEndpoint, requestContent, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
                    {
                        _cachedToken = tokenElement.GetString();
                        _httpClient.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", _cachedToken);
                        
                        _logger.LogInformation("[StateMigration] Successfully obtained authentication token");
                        return;
                    }
                }

                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("[StateMigration] Failed to obtain token: {StatusCode} - {Reason} - {Content}", 
                    response.StatusCode, response.ReasonPhrase, errorContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StateMigration] Error fetching authentication token");
            }
        }

        // If no token available, log warning but continue (might be public API)
        _logger.LogWarning("[StateMigration] No authentication token available, requests may fail");
    }

    /// <summary>
    /// Execute migration for specified collection types
    /// </summary>
    public async Task<MigrationResult> ExecuteAsync(
        List<string>? collectionTypes = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogInformation("[StateMigration] Job is disabled, skipping execution");
            return new MigrationResult { IsEnabled = false };
        }

        _logger.LogInformation("[StateMigration] Starting migration at {Time}", DateTime.UtcNow);

        var result = new MigrationResult
        {
            StartedAt = DateTime.UtcNow,
            CollectionTypes = collectionTypes ?? new List<string>()
        };

        try
        {
            // Ensure authenticated before making API calls
            await EnsureAuthenticatedAsync(cancellationToken);

            // 1. Get collections list from old system API
            var collections = await GetCollectionsAsync(cancellationToken);
            
            if (collections.Count == 0)
            {
                _logger.LogWarning("[StateMigration] No collections found from old system API");
                result.Error = "No collections found";
                return result;
            }

            _logger.LogInformation("[StateMigration] Found {Count} collections", collections.Count);

            // 2. Filter collections to migrate
            var collectionsToMigrate = collections;
            if (collectionTypes != null && collectionTypes.Count > 0)
            {
                collectionsToMigrate = collections.Where(c =>
                    collectionTypes.Any(t => 
                        c.TypeName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        c.CollectionName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            if (collectionsToMigrate.Count == 0)
            {
                _logger.LogWarning("[StateMigration] No matching collections found");
                result.Error = "No matching collections found";
                return result;
            }

            _logger.LogInformation("[StateMigration] Migrating {Count} collections", collectionsToMigrate.Count);

            // 3. Migrate each collection
            foreach (var collection in collectionsToMigrate)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("[StateMigration] Migration cancelled");
                    result.Cancelled = true;
                    break;
                }

                try
                {
                    var collectionResult = await MigrateCollectionAsync(collection, cancellationToken);
                    result.TotalRecords += collectionResult.TotalRecords;
                    result.SuccessCount += collectionResult.SuccessCount;
                    result.FailedCount += collectionResult.FailedCount;
                    result.CollectionResults.Add(collectionResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StateMigration] Error migrating collection {CollectionName}", 
                        collection.CollectionName);
                    result.FailedCount++;
                }
            }

            result.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "[StateMigration] Completed: Total={Total}, Success={Success}, Failed={Failed}, Duration={Duration}",
                result.TotalRecords, result.SuccessCount, result.FailedCount, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StateMigration] Fatal error in migration job");
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
            throw; // Re-throw to let Hangfire handle retry
        }
    }

    /// <summary>
    /// Get collections list from old system API
    /// </summary>
    private async Task<List<CollectionInfo>> GetCollectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_options.OldSystemApiBaseUrl}/api/admin/export/collections";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                var collections = JsonSerializer.Deserialize<List<CollectionInfo>>(
                    dataElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return collections ?? new List<CollectionInfo>();
            }

            return new List<CollectionInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StateMigration] Failed to get collections from API");
            throw;
        }
    }

    /// <summary>
    /// Migrate a single collection
    /// </summary>
    private async Task<CollectionMigrationResult> MigrateCollectionAsync(
        CollectionInfo collection,
        CancellationToken cancellationToken)
    {
        var result = new CollectionMigrationResult
        {
            CollectionName = collection.CollectionName,
            TypeName = collection.TypeName
        };

        _logger.LogInformation("[StateMigration] Migrating {TypeName} ({Count} records)...", 
            collection.TypeName, collection.Count);

        // Export all records from old system (with pagination)
        var allRecords = await ExportAllRecordsAsync(collection.CollectionName, cancellationToken);
        result.TotalRecords = allRecords.Count;

        if (allRecords.Count == 0)
        {
            _logger.LogInformation("[StateMigration] No records found in {TypeName}", collection.TypeName);
            return result;
        }

        // Convert and write records
        var converter = GetConverter(collection.TypeName);
        if (converter == null)
        {
            _logger.LogWarning("[StateMigration] No converter found for {TypeName}, skipping", collection.TypeName);
            result.FailedCount = allRecords.Count;
            return result;
        }

        foreach (var record in allRecords)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Convert Agent ID format
                var newAgentId = ConvertAgentId(record.Id, collection.TypeName);
                
                // Convert State
                var newState = converter.Convert(record.State);
                if (newState == null)
                {
                    result.FailedCount++;
                    continue;
                }

                // Write to new database
                var success = await WriteStateAsync(newAgentId, newState, collection.TypeName, cancellationToken);
                
                if (success)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.FailedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[StateMigration] Error converting record {Id}", record.Id);
                result.FailedCount++;
            }
        }

        _logger.LogInformation(
            "[StateMigration] {TypeName}: Success={Success}, Failed={Failed}",
            collection.TypeName, result.SuccessCount, result.FailedCount);

        return result;
    }

    /// <summary>
    /// Export all records from a collection (with pagination)
    /// </summary>
    private async Task<List<ExportedRecord>> ExportAllRecordsAsync(
        string collectionName,
        CancellationToken cancellationToken)
    {
        var allRecords = new List<ExportedRecord>();
        int skip = 0;
        const int limit = 1000;

        while (true)
        {
            var url = $"{_options.OldSystemApiBaseUrl}/api/admin/export/grain" +
                $"?collection={Uri.EscapeDataString(collectionName)}&skip={skip}&limit={limit}";
            
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("records", out var recordsElement))
            {
                var records = JsonSerializer.Deserialize<List<ExportedRecord>>(
                    recordsElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (records == null || records.Count == 0)
                    break;

                allRecords.AddRange(records);

                // Check if there are more records
                if (records.Count < limit)
                    break;

                skip += limit;
            }
            else
            {
                break;
            }
        }

        return allRecords;
    }

    /// <summary>
    /// Get converter for a specific type
    /// </summary>
    private IStateConverter? GetConverter(string typeName)
    {
        // Extract short type name for matching
        var shortName = ExtractShortTypeName(typeName);
        
            return shortName switch
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
                "AIAgentStatusProxy" => new AIAgentStatusProxyStateConverter(),
                _ => null
            };
    }

    /// <summary>
    /// Convert Agent ID from old format to new format
    /// </summary>
    private string ConvertAgentId(string oldId, string typeName)
    {
        if (string.IsNullOrWhiteSpace(oldId))
            return oldId;

        // If already new format (contains ':'), return as is
        if (oldId.Contains(':'))
            return oldId;

        // Parse old format: "FullTypeName/Guid"
        var parts = oldId.Split('/');
        if (parts.Length != 2)
            return oldId;

        var guid = parts[1];
        
        // Extract short type name
        var shortName = ExtractShortTypeName(typeName);
        
        return $"{shortName}:{guid}";
    }

    private string ExtractShortTypeName(string fullTypeName)
    {
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot >= 0 ? fullTypeName[(lastDot + 1)..] : fullTypeName;
    }

    /// <summary>
    /// Write State to new database (EventWave format)
    /// </summary>
    private async Task<bool> WriteStateAsync(
        string agentId,
        IMessage state,
        string agentTypeName,
        CancellationToken cancellationToken)
    {
        try
        {
            var database = _mongoClient.GetDatabase(_databaseName);
            // Use the same collection naming convention as MongoDBStateStore: agent_states_{StateTypeName}
            // For UserStatisticsGAgent, State type is UserStatisticsState, so collection is agent_states_UserStatisticsState
            var stateTypeName = state.GetType().Name; // e.g., "UserStatisticsState"
            var collectionName = $"agent_states_{stateTypeName}";
            var collection = database.GetCollection<BsonDocument>(collectionName);

            // Serialize Protobuf State
            var stateBytes = state.ToByteArray();

            // Use the same document structure as AgentStateDocument
            var document = new BsonDocument
            {
                { "AgentId", agentId },
                { "StateData", new BsonBinaryData(stateBytes, BsonBinarySubType.Binary) },
                { "StateType", stateTypeName },
                { "Version", 1L },
                { "UpdatedAt", DateTime.UtcNow }
            };

            var filter = Builders<BsonDocument>.Filter.Eq("AgentId", agentId);
            var options = new ReplaceOptions { IsUpsert = true };

            await collection.ReplaceOneAsync(filter, document, options, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StateMigration] Failed to write state for {AgentId}", agentId);
            return false;
        }
    }
}

/// <summary>
/// State converter interface
/// </summary>
public interface IStateConverter
{
    IMessage? Convert(Dictionary<string, object>? oldState);
}

/// <summary>
/// UserStatistics State converter
/// </summary>
public class UserStatisticsStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object>? oldState)
    {
        if (oldState == null)
            return new UserStatisticsState();

        var newState = new UserStatisticsState();

        // UserId: Guid -> string
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = userIdObj?.ToString();
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
            {
                newState.UserId = guid.ToString("D");
            }
        }

        // IsInitialized: bool
        if (oldState.TryGetValue("IsInitialized", out var isInitObj))
        {
            newState.IsInitialized = ConvertToBool(isInitObj);
        }

        // IsRealUser: bool
        if (oldState.TryGetValue("IsRealUser", out var isRealUserObj))
        {
            newState.IsRealUser = ConvertToBool(isRealUserObj);
        }

        // AppRatings: Dictionary<string, AppRatingInfo> -> map<string, AppRatingInfo>
        if (oldState.TryGetValue("AppRatings", out var appRatingsObj) && appRatingsObj != null)
        {
            Dictionary<string, object>? appRatingsDict = null;
            
            if (appRatingsObj is Dictionary<string, object> dict)
            {
                appRatingsDict = dict;
            }
            else if (appRatingsObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                appRatingsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
            }

            if (appRatingsDict != null)
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
        }

        return newState;
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
}

/// <summary>
/// AnonymousUser State converter
/// </summary>
public class AnonymousUserStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object>? oldState)
    {
        if (oldState == null)
            return new AnonymousUserState();

        var newState = new AnonymousUserState();

        if (oldState.TryGetValue("UserHashId", out var userHashIdObj))
            newState.UserHashId = ConvertToString(userHashIdObj);

        if (oldState.TryGetValue("CurrentSessionId", out var sessionIdObj))
        {
            var sessionIdStr = ConvertToString(sessionIdObj);
            if (!string.IsNullOrEmpty(sessionIdStr) && Guid.TryParse(sessionIdStr, out _))
                newState.CurrentSessionId = sessionIdStr;
        }

        if (oldState.TryGetValue("ChatCount", out var chatCountObj))
            newState.ChatCount = ConvertToInt32(chatCountObj);

        if (oldState.TryGetValue("LastChatTime", out var lastChatTimeObj))
        {
            var dt = ConvertToDateTime(lastChatTimeObj);
            if (dt.HasValue)
                newState.LastChatTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("CurrentGuider", out var guiderObj))
        {
            var guiderStr = ConvertToString(guiderObj);
            if (!string.IsNullOrEmpty(guiderStr))
                newState.CurrentGuider = guiderStr;
        }

        if (oldState.TryGetValue("CurrentSessionUsed", out var sessionUsedObj))
            newState.CurrentSessionUsed = ConvertToBool(sessionUsedObj);

        return newState;
    }

    private string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
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
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
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
}

/// <summary>
/// Migration result
/// </summary>
public class MigrationResult
{
    public bool IsEnabled { get; set; } = true;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
    public List<string> CollectionTypes { get; set; } = new();
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
    public List<CollectionMigrationResult> CollectionResults { get; set; } = new();
}

public class CollectionMigrationResult
{
    public string CollectionName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}

public class CollectionInfo
{
    public string CollectionName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ExportedRecord
{
    public string Id { get; set; } = string.Empty;
    public string? ETag { get; set; }
    public Dictionary<string, object>? State { get; set; }
}
