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
    /// Always fetches token dynamically from TokenEndpoint
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        // If token is already cached, use it
        if (!string.IsNullOrEmpty(_cachedToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _cachedToken);
            return;
        }

        // Fetch token from TokenEndpoint
        if (string.IsNullOrEmpty(_options.TokenEndpoint) ||
            string.IsNullOrEmpty(_options.Username) ||
            string.IsNullOrEmpty(_options.Password))
        {
            _logger.LogWarning("[StateMigration] Token credentials not configured (TokenEndpoint, Username, Password required)");
            return;
        }

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
            _logger.LogError("[StateMigration] Failed to obtain token: {StatusCode} - {Reason} - {Content}", 
                response.StatusCode, response.ReasonPhrase, errorContent);
            throw new InvalidOperationException($"Failed to obtain authentication token: {response.StatusCode}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "[StateMigration] Error fetching authentication token");
            throw;
        }
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

            // 1. Get collections list - use FixedCollections if configured, otherwise call API
            List<CollectionInfo> collections;
            
            if (_options.FixedCollections.Count > 0)
            {
                _logger.LogInformation("[StateMigration] Using {Count} fixed collections from config", 
                    _options.FixedCollections.Count);
                    
                collections = _options.FixedCollections.Select(name => new CollectionInfo
                {
                    CollectionName = name,
                    TypeName = ExtractShortTypeName(name)
                }).ToList();
            }
            else
            {
                collections = await GetCollectionsAsync(cancellationToken);
            }
            
            if (collections.Count == 0)
            {
                _logger.LogWarning("[StateMigration] No collections found");
                result.Error = "No collections found";
                return result;
            }

            _logger.LogInformation("[StateMigration] Found {Count} collections", collections.Count);

            // 2. Filter collections to migrate
            var collectionsToMigrate = collections;
            
            // Apply SkipCollections filter first
            if (_options.SkipCollections.Count > 0)
            {
                collectionsToMigrate = collectionsToMigrate.Where(c =>
                    !_options.SkipCollections.Any(skip => 
                        c.TypeName.Contains(skip, StringComparison.OrdinalIgnoreCase) ||
                        c.CollectionName.Contains(skip, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                    
                _logger.LogInformation("[StateMigration] After SkipCollections filter: {Count} collections", 
                    collectionsToMigrate.Count);
            }
            
            // Then apply user-specified filter if provided
            if (collectionTypes != null && collectionTypes.Count > 0)
            {
                collectionsToMigrate = collectionsToMigrate.Where(c =>
                    collectionTypes.Any(t => 
                        c.TypeName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        c.CollectionName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            if (collectionsToMigrate.Count == 0)
            {
                _logger.LogWarning("[StateMigration] No matching collections found after filtering");
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
    /// Migrate a single collection (streaming mode - fetch and process batch by batch)
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

        // Get converter first
        var converter = GetConverter(collection.TypeName);
        if (converter == null)
        {
            _logger.LogWarning("[StateMigration] No converter found for {TypeName}, skipping", collection.TypeName);
            return result;
        }

        var jsonFormatter = new JsonFormatter(JsonFormatter.Settings.Default);
        int skip = 0;
        const int limit = 500; // Increased batch size for better performance
        const int bulkWriteSize = 100; // Bulk write size for MongoDB
        int batchNumber = 0;
        int sampleLogged = 0;
        var bulkWriteBuffer = new List<(string AgentId, IMessage State, string AgentTypeName)>();

        // Stream processing: fetch and process batch by batch
        while (!cancellationToken.IsCancellationRequested)
        {
            batchNumber++;
            var fetchStartTime = DateTime.UtcNow;
            var (records, hasMore) = await FetchBatchAsync(collection.CollectionName, skip, limit, cancellationToken);
            var fetchDuration = (DateTime.UtcNow - fetchStartTime).TotalMilliseconds;
            
            if (records == null || records.Count == 0)
            {
                if (batchNumber == 1)
                    _logger.LogInformation("[StateMigration] No records found in {TypeName}", collection.TypeName);
                if (!hasMore)
                    break;
                // If no records but hasMore, continue to next batch (some records were skipped)
                skip += limit;
                continue;
            }

            _logger.LogInformation("[StateMigration] [{TypeName}] Processing batch {Batch}: {Count} records (skip={Skip}, hasMore={HasMore}, fetchTime={FetchTime}ms)",
                collection.TypeName, batchNumber, records.Count, skip, hasMore, fetchDuration.ToString("F2"));

            result.TotalRecords += records.Count;

            // Process each record in the batch
            foreach (var record in records)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Log sample records (first 3 only)
                    if (sampleLogged < 3)
                    {
                        LogSampleRecord(collection.TypeName, sampleLogged + 1, record, converter, jsonFormatter);
                        sampleLogged++;
                    }

                    // Convert Agent ID format
                    var newAgentId = ConvertAgentId(record.Id, collection.TypeName);
                    var stateIsEmpty = record.State == null || record.State.Count == 0;
                    
                    // Convert State
                    var newState = converter.Convert(record.State);
                    if (newState == null)
                    {
                        _logger.LogWarning(
                            "[StateMigration] [{TypeName}] Converter returned null for Id={Id}",
                            collection.TypeName, record.Id);
                        result.FailedCount++;
                        continue;
                    }

                    // Log first 3 successful conversions
                    if (result.SuccessCount < 3)
                    {
                        var stateBytes = newState.ToByteArray();
                        var newStateJson = jsonFormatter.Format(newState);
                        if (newStateJson.Length > 500) newStateJson = newStateJson[..500] + "...(truncated)";
                        
                        _logger.LogInformation(
                            "[StateMigration] [{TypeName}] Converted: OldId={OldId} -> NewId={NewId}, " +
                            "NewStateType={StateType}, NewStateBytes={Bytes}",
                            collection.TypeName, record.Id, newAgentId, 
                            newState.GetType().FullName, stateBytes.Length);
                    }

                    // Handle agent type name mapping
                    var targetAgentTypeName = MapAgentTypeName(collection.TypeName);
                    if (targetAgentTypeName != collection.TypeName && newAgentId.Contains(':'))
                    {
                        var parts = newAgentId.Split(':', 2);
                        if (parts.Length == 2)
                            newAgentId = $"{targetAgentTypeName}:{parts[1]}";
                    }

                    // Add to bulk write buffer
                    bulkWriteBuffer.Add((newAgentId, newState, targetAgentTypeName));
                    
                    // Bulk write when buffer reaches threshold
                    if (bulkWriteBuffer.Count >= bulkWriteSize)
                    {
                        var writeStartTime = DateTime.UtcNow;
                        var writeResult = await BulkWriteStateAsync(bulkWriteBuffer, cancellationToken);
                        var writeDuration = (DateTime.UtcNow - writeStartTime).TotalMilliseconds;
                        
                        result.SuccessCount += writeResult.SuccessCount;
                        result.FailedCount += writeResult.FailedCount;
                        
                        _logger.LogInformation("[StateMigration] [{TypeName}] Bulk write: {Success} success, {Failed} failed, time={Time}ms",
                            collection.TypeName, writeResult.SuccessCount, writeResult.FailedCount, writeDuration.ToString("F2"));
                        
                        bulkWriteBuffer.Clear();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[StateMigration] [{TypeName}] Error converting record {Id}", 
                        collection.TypeName, record.Id);
                    result.FailedCount++;
                }
            }
            
            // Write remaining records in buffer
            if (bulkWriteBuffer.Count > 0)
            {
                var writeStartTime = DateTime.UtcNow;
                var writeResult = await BulkWriteStateAsync(bulkWriteBuffer, cancellationToken);
                var writeDuration = (DateTime.UtcNow - writeStartTime).TotalMilliseconds;
                
                result.SuccessCount += writeResult.SuccessCount;
                result.FailedCount += writeResult.FailedCount;
                
                _logger.LogInformation("[StateMigration] [{TypeName}] Final bulk write: {Success} success, {Failed} failed, time={Time}ms",
                    collection.TypeName, writeResult.SuccessCount, writeResult.FailedCount, writeDuration.ToString("F2"));
                
                bulkWriteBuffer.Clear();
            }
            
            // Log batch completion
            _logger.LogInformation("[StateMigration] [{TypeName}] Batch {Batch} done: success={Success}, failed={Failed}",
                collection.TypeName, batchNumber, result.SuccessCount, result.FailedCount);

            // Check if there are more records using hasMore flag
            if (!hasMore)
                break;

            skip += limit;
            
            // Delay between batches
            await Task.Delay(_options.BatchDelayMs, cancellationToken);
        }

        _logger.LogInformation(
            "[StateMigration] {TypeName}: Total={Total}, Success={Success}, Failed={Failed}",
            collection.TypeName, result.TotalRecords, result.SuccessCount, result.FailedCount);

        return result;
    }

    /// <summary>
    /// Log sample record for debugging
    /// </summary>
    private void LogSampleRecord(string typeName, int index, ExportedRecord record, 
        IStateConverter converter, JsonFormatter jsonFormatter)
    {
        _logger.LogInformation(
            "[StateMigration] [{TypeName}] Sample record {Index}: Id={Id}, StateKeys=[{StateKeys}]",
            typeName, index, record.Id,
            record.State != null ? string.Join(",", record.State.Keys.Take(10)) : "N/A");
            
        if (record.State != null)
        {
            try
            {
                var originalJson = JsonSerializer.Serialize(record.State, 
                    new JsonSerializerOptions { WriteIndented = false });
                if (originalJson.Length > 800) originalJson = originalJson[..800] + "...(truncated)";
                _logger.LogInformation("[StateMigration] [{TypeName}] Sample {Index} OriginalJson: {Json}",
                    typeName, index, originalJson);
                    
                var convertedState = converter.Convert(record.State);
                if (convertedState != null)
                {
                    var newStateJson = jsonFormatter.Format(convertedState);
                    if (newStateJson.Length > 800) newStateJson = newStateJson[..800] + "...(truncated)";
                    _logger.LogInformation("[StateMigration] [{TypeName}] Sample {Index} ConvertedJson: {Json}",
                        typeName, index, newStateJson);
                }
            }
            catch { /* Ignore logging errors */ }
        }
    }

    /// <summary>
    /// Fetch a single batch of records from old system API
    /// Returns (records, hasMore) tuple
    /// </summary>
    private async Task<(List<ExportedRecord>? Records, bool HasMore)> FetchBatchAsync(
        string collectionName, int skip, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_options.OldSystemApiBaseUrl}/api/admin/export/grain" +
                $"?collection={Uri.EscapeDataString(collectionName)}&skip={skip}&limit={limit}";
            
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.ValueKind == JsonValueKind.Object)
            {
                // Parse hasMore flag
                var hasMore = false;
                if (dataElement.TryGetProperty("hasMore", out var hasMoreElement))
                {
                    hasMore = hasMoreElement.ValueKind == JsonValueKind.True;
                }
                
                // Parse records
                if (dataElement.TryGetProperty("records", out var recordsElement) &&
                    recordsElement.ValueKind == JsonValueKind.Array)
                {
                    var records = JsonSerializer.Deserialize<List<ExportedRecord>>(
                        recordsElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return (records, hasMore);
                }
                
                return (null, hasMore);
            }
            
            return (null, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StateMigration] Error fetching batch: collection={Collection}, skip={Skip}", 
                collectionName, skip);
            return (null, false);
        }
    }

    /// <summary>
    /// Export all records from a collection (with pagination)
    /// </summary>
    /// <summary>
    /// Get converter for a specific type
    /// </summary>
    private IStateConverter? GetConverter(string typeName)
    {
        // Extract short type name for matching
        var shortName = ExtractShortTypeName(typeName);
        
        if (shortName == "ChatGAgentManager")
            shortName = "ChatManagerGAgent";

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
            "InviteCodeGAgent" => new InviteCodeStateConverter(),
            "UserFeedbackGAgent" => new UserFeedbackStateConverter(),
            "UserInfoCollectionGAgent" => new UserInfoCollectionStateConverter(),
            "LumenUserProfileGAgent" => new LumenUserProfileStateConverter(),
            "LumenPredictionGAgent" => new LumenPredictionStateConverter(),
            "LumenDailyYearlyHistoryGAgent" => new LumenDailyYearlyHistoryStateConverter(),
            "LumenFeedbackGAgent" => new LumenFeedbackStateConverter(),
            "UserBillingGAgent" => new UserBillingStateConverter(),
            "GoogleAuthGAgent" => new GoogleAuthStateConverter(),
            "GoogleIdentityBindingGAgent" => new GoogleIdentityBindingStateConverter(),
            "AIAgentStatusProxy" => new AIAgentStatusProxyStateConverter(),
            "TwitterAuthGAgent" => new TwitterAuthStateConverter(),
            "TwitterIdentityBindingGAgent" => new TwitterIdentityBindingStateConverter(),
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
    /// Map old agent type name to new agent type name (for refactored agents)
    /// </summary>
    private string MapAgentTypeName(string oldTypeName)
    {
        var shortName = ExtractShortTypeName(oldTypeName);
        
        // Handle special cases where agent was refactored
        return shortName switch
        {
            "UserBillingGAgent" => "PaymentIndexGAgent",
            _ => shortName
        };
    }

    /// <summary>
    /// Bulk write states to MongoDB using BulkWrite for better performance
    /// </summary>
    private async Task<(int SuccessCount, int FailedCount)> BulkWriteStateAsync(
        List<(string AgentId, IMessage State, string AgentTypeName)> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
            return (0, 0);

        int successCount = 0;
        int failedCount = 0;

        try
        {
            var database = _mongoClient.GetDatabase(_databaseName);
            
            // Group by StateType to write to correct collections
            var groupedByStateType = records.GroupBy(r => r.State.GetType().Name);

            foreach (var group in groupedByStateType)
            {
                var stateTypeShortName = group.Key;
                var collectionName = $"agent_states_{stateTypeShortName}";
                var collection = database.GetCollection<BsonDocument>(collectionName);
                var stateTypeFullName = group.First().State.GetType().FullName ?? stateTypeShortName;

                var bulkOps = new List<WriteModel<BsonDocument>>();

                foreach (var (agentId, state, _) in group)
                {
                    try
                    {
                        var stateBytes = state.ToByteArray();
                        var document = new BsonDocument
                        {
                            { "_id", agentId },
                            { "StateData", new BsonBinaryData(stateBytes, BsonBinarySubType.Binary) },
                            { "StateType", stateTypeFullName },
                            { "Version", 1L },
                            { "UpdatedAt", DateTime.UtcNow }
                        };

                        var filter = Builders<BsonDocument>.Filter.Eq("_id", agentId);
                        var replaceOneModel = new ReplaceOneModel<BsonDocument>(filter, document)
                        {
                            IsUpsert = true
                        };
                        bulkOps.Add(replaceOneModel);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[StateMigration] Error preparing bulk write for {AgentId}", agentId);
                        failedCount++;
                    }
                }

                if (bulkOps.Count > 0)
                {
                    try
                    {
                        var bulkResult = await collection.BulkWriteAsync(bulkOps, 
                            new BulkWriteOptions { IsOrdered = false }, cancellationToken);
                        // ReplaceOneModel with IsUpsert=true: ModifiedCount for updates, InsertedCount for inserts
                        var totalSuccess = bulkResult.ModifiedCount + bulkResult.InsertedCount;
                        successCount += (int)totalSuccess;
                        failedCount += bulkOps.Count - (int)totalSuccess;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[StateMigration] Bulk write failed for collection {Collection}", collectionName);
                        failedCount += bulkOps.Count;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StateMigration] Bulk write operation failed");
            failedCount += records.Count;
        }

        return (successCount, failedCount);
    }

    /// <summary>
    /// Write State to new database (matching MongoDBStateStore/AgentStateDocument format)
    /// 
    /// AgentStateDocument uses:
    /// - _id: AgentId (via [BsonId] attribute)
    /// - StateType: Full type name (typeof(TState).FullName)
    /// 
    /// Note: This method is kept for backward compatibility but BulkWriteStateAsync is preferred
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
            
            // Collection naming: agent_states_{StateTypeName} (short name)
            var stateTypeShortName = state.GetType().Name; // e.g., "UserStatisticsState"
            var collectionName = $"agent_states_{stateTypeShortName}";
            var collection = database.GetCollection<BsonDocument>(collectionName);

            // StateType should be full type name (matching MongoDBStateStore)
            var stateTypeFullName = state.GetType().FullName ?? stateTypeShortName;

            // Serialize Protobuf State
            var stateBytes = state.ToByteArray();

            // Match AgentStateDocument structure exactly:
            // - _id: AgentId (not a separate AgentId field!)
            // - StateType: Full type name
            var document = new BsonDocument
            {
                { "_id", agentId },  // AgentId as _id (matching [BsonId] attribute)
                { "StateData", new BsonBinaryData(stateBytes, BsonBinarySubType.Binary) },
                { "StateType", stateTypeFullName },  // Full type name
                { "Version", 1L },
                { "UpdatedAt", DateTime.UtcNow }
            };

            // Use _id for filter (not AgentId)
            var filter = Builders<BsonDocument>.Filter.Eq("_id", agentId);
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
    IMessage? Convert(Dictionary<string, object?>? oldState);
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
    public Dictionary<string, object?>? State { get; set; }
}
