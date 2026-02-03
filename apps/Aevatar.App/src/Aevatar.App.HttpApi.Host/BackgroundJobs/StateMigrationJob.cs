using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.Agents.GodGPT.Protos.Anonymous;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Payment.Agents;
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
    private readonly IStateIndexService? _stateIndexService;
    private readonly IInvitationService? _invitationService;
    private readonly IGAgentActorFactory? _actorFactory;
    private string? _cachedToken;

    public StateMigrationJob(
        IHttpClientFactory httpClientFactory,
        IMongoClient mongoClient,
        ILogger<StateMigrationJob> logger,
        IOptions<StateMigrationOptions> options,
        IConfiguration configuration,
        IInvitationService? invitationService = null,
        IStateIndexService? stateIndexService = null,
        IGAgentActorFactory? actorFactory = null)
    {
        _httpClientFactory = httpClientFactory;
        _httpClient = httpClientFactory.CreateClient();
        _mongoClient = mongoClient;
        _logger = logger;
        _options = options.Value;
        _stateIndexService = stateIndexService;
        _invitationService = invitationService;
        _actorFactory = actorFactory;
        
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
        const int limit = 500; // Batch size for pagination
        const int bulkWriteSize = 100; // Bulk write size for MongoDB
        int batchNumber = 0;
        int sampleLogged = 0;
        var bulkWriteBuffer = new List<(string AgentId, IMessage State, string AgentTypeName)>();
        string? cursor = null; // Cursor for pagination (null for first page)

        // Stream processing: fetch and process batch by batch using cursor pagination
        while (!cancellationToken.IsCancellationRequested)
        {
            batchNumber++;
            var fetchStartTime = DateTime.UtcNow;
            var (records, hasMore, nextCursor) = await FetchBatchAsync(collection.CollectionName, limit, cursor, cancellationToken);
            var fetchDuration = (DateTime.UtcNow - fetchStartTime).TotalMilliseconds;
            
            if (records == null || records.Count == 0)
            {
                if (batchNumber == 1)
                    _logger.LogInformation("[StateMigration] No records found in {TypeName}", collection.TypeName);
                if (!hasMore)
                    break;
                // If no records but hasMore, continue to next batch (some records were skipped)
                cursor = nextCursor;
                continue;
            }

            _logger.LogInformation("[StateMigration] [{TypeName}] Processing batch {Batch}: {Count} records (cursor={Cursor}, hasMore={HasMore}, fetchTime={FetchTime}ms)",
                collection.TypeName, batchNumber, records.Count, cursor ?? "skip=0", hasMore, fetchDuration.ToString("F2"));

            result.TotalRecords += records.Count;
            
            // Update cursor for next iteration
            cursor = nextCursor;

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
                    
                    // Handle AdditionalPaymentRecords from UserBilling converters
                    if (converter is UserBillingGrainStateConverter grainConverter)
                    {
                        foreach (var (agentId, paymentState) in grainConverter.AdditionalPaymentRecords)
                        {
                            bulkWriteBuffer.Add((agentId, paymentState, "PaymentRecordGAgent"));
                        }
                    }
                    else if (converter is UserBillingStateConverter billingConverter)
                    {
                        foreach (var (agentId, paymentState) in billingConverter.AdditionalPaymentRecords)
                        {
                            bulkWriteBuffer.Add((agentId, paymentState, "PaymentRecordGAgent"));
                        }
                    }
                    
                    // Handle AdditionalUserDeviceRecords from ChatManager converter
                    if (converter is ChatManagerStateConverter chatManagerConverter)
                    {
                        foreach (var (agentId, deviceState) in chatManagerConverter.AdditionalUserDeviceRecords)
                        {
                            bulkWriteBuffer.Add((agentId, deviceState, "UserDeviceGAgent"));
                        }
                    }
                    
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
            
            // Cursor is already updated above (line 348), no need to increment skip
            
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
    /// Fetch a batch of records from old system API using cursor pagination
    /// Returns (records, hasMore, nextCursor) tuple
    /// </summary>
    private async Task<(List<ExportedRecord>? Records, bool HasMore, string? NextCursor)> FetchBatchAsync(
        string collectionName, int limit, string? cursor, CancellationToken cancellationToken)
    {
        try
        {
            // Build URL with cursor (if provided) or skip=0 (for first page)
            var urlBuilder = new System.Text.StringBuilder();
            urlBuilder.Append($"{_options.OldSystemApiBaseUrl}/api/admin/export/grain");
            urlBuilder.Append($"?collection={Uri.EscapeDataString(collectionName)}");
            urlBuilder.Append($"&limit={limit}");
            
            if (!string.IsNullOrEmpty(cursor))
            {
                // Use cursor for subsequent pages (more efficient)
                urlBuilder.Append($"&cursor={Uri.EscapeDataString(cursor)}");
            }
            else
            {
                // Use skip=0 for first page to get initial cursor
                urlBuilder.Append("&skip=0");
            }
            
            var url = urlBuilder.ToString();
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
                
                // Parse nextCursor
                string? nextCursor = null;
                if (dataElement.TryGetProperty("nextCursor", out var nextCursorElement) &&
                    nextCursorElement.ValueKind == JsonValueKind.String)
                {
                    nextCursor = nextCursorElement.GetString();
                }
                
                // Parse records
                if (dataElement.TryGetProperty("records", out var recordsElement) &&
                    recordsElement.ValueKind == JsonValueKind.Array)
                {
                    var records = JsonSerializer.Deserialize<List<ExportedRecord>>(
                        recordsElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return (records, hasMore, nextCursor);
                }
                
                return (null, hasMore, nextCursor);
            }
            
            return (null, false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StateMigration] Error fetching batch: collection={Collection}, cursor={Cursor}", 
                collectionName, cursor ?? "null");
            return (null, false, null);
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
            // Orleans grain states (converted to agent states)
            "ShareState" => new ShareStateConverter(),
            "UserPaymentState" => new UserPaymentStateConverter(),
            "UserBillingState" => new UserBillingGrainStateConverter(), // Orleans grain state
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

        var guidStr = parts[1];
        
        // Normalize GUID to standard format with dashes: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        if (Guid.TryParse(guidStr, out var guid))
            guidStr = guid.ToString("D");
        
        // Extract short type name
        var shortName = ExtractShortTypeName(typeName);
        
        return $"{shortName}:{guidStr}";
    }

    private string ExtractShortTypeName(string fullTypeName)
    {
        // Handle Orleans grain state names (e.g., "OrleansgodgptprodShareState" -> "ShareState")
        if (fullTypeName.StartsWith("Orleansgodgptprod", StringComparison.OrdinalIgnoreCase))
        {
            return fullTypeName.Substring("Orleansgodgptprod".Length);
        }
        
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
            // ChatManager renamed from ChatGAgentManager to ChatManagerGAgent
            "ChatGAgentManager" => "ChatManagerGAgent",
            "UserBillingGAgent" => "PaymentIndexGAgent",
            // Orleans grain states mapped to agent states
            "ShareState" => "ShareLinkGAgent",
            "UserPaymentState" => "PaymentRecordGAgent",
            "UserBillingState" => "PaymentIndexGAgent", // Orleans grain state -> PaymentIndexGAgent
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
                            { "Version", 1L }, // Must be > 0 for EventSourcing agents to load snapshot
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

    #region Elasticsearch Sync

    /// <summary>
    /// Sync MongoDB state data to Elasticsearch for specified collections.
    /// Reads from agent_states_{collectionName} and indexes to ES.
    /// </summary>
    /// <param name="collectionNames">Collection names to sync (e.g., "UserDeviceState", "PayRecordState")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync result with success/failure counts</returns>
    public async Task<EsSyncResult> SyncToElasticsearchAsync(
        List<string> collectionNames,
        CancellationToken cancellationToken = default)
    {
        if (_stateIndexService == null)
        {
            _logger.LogError("[ES Sync] IStateIndexService is not available. Check Elasticsearch configuration.");
            return new EsSyncResult { Error = "Elasticsearch service not configured" };
        }

        var result = new EsSyncResult { StartedAt = DateTime.UtcNow };
        _logger.LogInformation("[ES Sync] Starting sync for collections: {Collections}", 
            string.Join(", ", collectionNames));

        var database = _mongoClient.GetDatabase(_databaseName);

        foreach (var collectionName in collectionNames)
        {
            var collectionResult = await SyncCollectionToEsAsync(
                database, collectionName, cancellationToken);
            result.CollectionResults.Add(collectionResult);
            result.TotalRecords += collectionResult.TotalRecords;
            result.SuccessCount += collectionResult.SuccessCount;
            result.FailedCount += collectionResult.FailedCount;
        }

        result.CompletedAt = DateTime.UtcNow;
        _logger.LogInformation(
            "[ES Sync] Completed. Total={Total}, Success={Success}, Failed={Failed}, Duration={Duration}",
            result.TotalRecords, result.SuccessCount, result.FailedCount, result.Duration);

        return result;
    }

    private async Task<EsCollectionSyncResult> SyncCollectionToEsAsync(
        IMongoDatabase database,
        string collectionName,
        CancellationToken cancellationToken)
    {
        var result = new EsCollectionSyncResult { CollectionName = collectionName };
        var mongoCollectionName = $"agent_states_{collectionName}";

        try
        {
            var collection = database.GetCollection<BsonDocument>(mongoCollectionName);
            var totalCount = await collection.CountDocumentsAsync(
                FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);
            
            result.TotalRecords = (int)totalCount;
            _logger.LogInformation("[ES Sync] [{Collection}] Found {Count} documents", 
                collectionName, totalCount);

            if (totalCount == 0)
                return result;

            // Process in batches
            var batchSize = _options.BatchSize > 0 ? _options.BatchSize : 1000;
            var processedCount = 0;
            var cursor = await collection.FindAsync(
                FilterDefinition<BsonDocument>.Empty,
                new FindOptions<BsonDocument> { BatchSize = batchSize },
                cancellationToken);

            var batch = new List<StateIndexDocument>();

            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var doc in cursor.Current)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var indexDoc = ConvertToStateIndexDocument(doc, collectionName);
                    if (indexDoc != null)
                    {
                        batch.Add(indexDoc);
                    }
                    else
                    {
                        result.FailedCount++;
                    }

                    // Write batch when full
                    if (batch.Count >= batchSize)
                    {
                        var (success, failed) = await WriteBatchToEsAsync(batch, cancellationToken);
                        result.SuccessCount += success;
                        result.FailedCount += failed;
                        processedCount += batch.Count;
                        
                        _logger.LogInformation(
                            "[ES Sync] [{Collection}] Progress: {Processed}/{Total}", 
                            collectionName, processedCount, totalCount);
                        
                        batch.Clear();
                    }
                }
            }

            // Write remaining batch
            if (batch.Count > 0)
            {
                var (success, failed) = await WriteBatchToEsAsync(batch, cancellationToken);
                result.SuccessCount += success;
                result.FailedCount += failed;
            }

            _logger.LogInformation(
                "[ES Sync] [{Collection}] Completed: Success={Success}, Failed={Failed}",
                collectionName, result.SuccessCount, result.FailedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ES Sync] [{Collection}] Error during sync", collectionName);
            result.Error = ex.Message;
        }

        return result;
    }

    private StateIndexDocument? ConvertToStateIndexDocument(BsonDocument doc, string collectionName)
    {
        try
        {
            var agentId = doc["_id"].AsString;
            var stateType = doc.Contains("StateType") ? doc["StateType"].AsString : null;
            var version = doc.Contains("Version") ? doc["Version"].ToInt64() : 0;
            var updatedAt = doc.Contains("UpdatedAt") ? doc["UpdatedAt"].ToUniversalTime() : DateTime.UtcNow;

            // Derive full AgentType from StateType for correct ES index naming
            // StateType: "Aevatar.Agents.GodGPT.Protos.UserDevice.UserDeviceState"
            // AgentType: "Aevatar.Agents.GodGPT.UserDevice.UserDeviceGAgent"
            var agentType = DeriveAgentTypeFromStateType(stateType, agentId, collectionName);

            // Parse StateData (Protobuf bytes) to extract properties
            var data = new Dictionary<string, object>();
            
            if (doc.Contains("StateData") && !string.IsNullOrEmpty(stateType))
            {
                var stateBytes = doc["StateData"].AsByteArray;
                var protoData = ParseProtobufToDict(stateBytes, stateType);
                if (protoData != null)
                {
                    foreach (var kvp in protoData)
                    {
                        data[kvp.Key] = kvp.Value;
                    }
                }
            }

            return new StateIndexDocument
            {
                AgentId = agentId,
                AgentType = agentType,
                Data = data,
                Version = version,
                IndexedAt = updatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ES Sync] Failed to convert document");
            return null;
        }
    }

    /// <summary>
    /// Derive full AgentType from StateType for ES index naming.
    /// StateType: "Aevatar.Agents.GodGPT.Protos.UserDevice.UserDeviceState"
    /// AgentType: "Aevatar.Agents.GodGPT.UserDevice.UserDeviceGAgent"
    /// </summary>
    private string DeriveAgentTypeFromStateType(string? stateType, string agentId, string collectionName)
    {
        if (!string.IsNullOrEmpty(stateType))
        {
            // Remove ".Protos" from the namespace and replace "State" suffix with "GAgent"
            var agentType = stateType.Replace(".Protos", "");
            
            // Replace State suffix with GAgent
            if (agentType.EndsWith("State"))
            {
                agentType = agentType.Substring(0, agentType.Length - 5) + "GAgent";
            }
            else if (agentType.EndsWith("StateProto"))
            {
                agentType = agentType.Substring(0, agentType.Length - 10) + "GAgent";
            }
            
            return agentType;
        }
        
        // Fallback: extract from agentId
        if (agentId.Contains(':'))
        {
            return agentId.Substring(0, agentId.IndexOf(':'));
        }
        
        return collectionName.Replace("State", "GAgent");
    }

    private Dictionary<string, object>? ParseProtobufToDict(byte[] stateBytes, string stateTypeName)
    {
        try
        {
            // Find the Protobuf type by name
            var stateType = FindProtobufType(stateTypeName);
            if (stateType == null)
            {
                _logger.LogWarning("[ES Sync] Could not find type: {TypeName}", stateTypeName);
                return null;
            }

            // Get the Parser property
            var parserProperty = stateType.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static);
            if (parserProperty == null)
            {
                _logger.LogWarning("[ES Sync] No Parser found for type: {TypeName}", stateTypeName);
                return null;
            }

            var parser = parserProperty.GetValue(null) as MessageParser;
            if (parser == null)
                return null;

            // Parse the bytes
            var message = parser.ParseFrom(stateBytes);
            
            // Extract properties to dictionary
            return ExtractPropertiesToDict(message, stateType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ES Sync] Failed to parse Protobuf for type: {TypeName}", stateTypeName);
            return null;
        }
    }

    private static readonly Dictionary<string, System.Type?> _typeCache = new();

    private System.Type? FindProtobufType(string fullTypeName)
    {
        if (_typeCache.TryGetValue(fullTypeName, out var cachedType))
            return cachedType;

        // Extract simple name from full type name
        var simpleName = fullTypeName.Contains('.')
            ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
            : fullTypeName;

        // Search all assemblies for the type
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<System.Type>(); } })
            .FirstOrDefault(t =>
                typeof(IMessage).IsAssignableFrom(t) &&
                !t.IsAbstract &&
                (t.FullName == fullTypeName || t.Name == simpleName));

        _typeCache[fullTypeName] = type;
        return type;
    }

    private Dictionary<string, object> ExtractPropertiesToDict(IMessage message, System.Type stateType)
    {
        var data = new Dictionary<string, object>();

        foreach (var property in stateType.GetProperties())
        {
            // Skip Protobuf internal properties
            if (property.Name is "Parser" or "Descriptor" or "MessageType" ||
                property.DeclaringType == typeof(IMessage) ||
                property.DeclaringType == typeof(object))
                continue;

            try
            {
                var value = property.GetValue(message);
                if (value == null) continue;

                // Convert to camelCase
                var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

                data[name] = value switch
                {
                    Timestamp ts => ts.ToDateTime(),
                    IMessage nested => JsonSerializer.Serialize(nested, _jsonOptions),
                    _ when IsBasicType(property.PropertyType) => value,
                    _ => JsonSerializer.Serialize(value, _jsonOptions)
                };
            }
            catch
            {
                // Skip properties that fail to extract
            }
        }

        return data;
    }

    private static bool IsBasicType(System.Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t == typeof(string) || t == typeof(DateTime) ||
               t == typeof(decimal) || t == typeof(Guid) || t == typeof(Timestamp);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private async Task<(int success, int failed)> WriteBatchToEsAsync(
        List<StateIndexDocument> batch,
        CancellationToken cancellationToken)
    {
        try
        {
            // Ensure indices exist for all agent types in the batch
            var agentTypes = batch.Select(d => d.AgentType).Distinct();
            foreach (var agentType in agentTypes)
            {
                await _stateIndexService!.EnsureIndexExistsAsync(agentType, null, cancellationToken);
            }

            await _stateIndexService!.IndexStateBatchAsync(batch, cancellationToken);
            _logger.LogDebug("[ES Sync] Successfully wrote {Count} documents to ES", batch.Count);
            return (batch.Count, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ES Sync] Batch write failed for {Count} documents", batch.Count);
            return (0, batch.Count);
        }
    }

    #endregion

    #region Single Record Migration Test

    /// <summary>
    /// Test migration of a single record by ID
    /// Fetches from old system, converts, and optionally writes to new system
    /// </summary>
    public async Task<SingleRecordMigrationResult> TestSingleRecordMigrationAsync(
        string collectionTypeName,
        string recordId,
        bool writeToDb = false,
        CancellationToken cancellationToken = default)
    {
        var result = new SingleRecordMigrationResult
        {
            CollectionTypeName = collectionTypeName,
            RecordId = recordId,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);

            // 1. Fetch single record from old system
            var record = await FetchSingleRecordAsync(collectionTypeName, recordId, cancellationToken);
            if (record == null)
            {
                result.Error = $"Record not found: {recordId} in {collectionTypeName}";
                return result;
            }

            result.OriginalState = record.State;
            result.OriginalStateJson = record.State != null 
                ? JsonSerializer.Serialize(record.State, new JsonSerializerOptions { WriteIndented = true })
                : null;

            // 2. Get converter
            var converter = GetConverter(collectionTypeName);
            if (converter == null)
            {
                result.Error = $"No converter found for {collectionTypeName}";
                return result;
            }

            result.ConverterUsed = converter.GetType().Name;

            // 3. Convert
            var newState = converter.Convert(record.State);
            if (newState == null)
            {
                result.Error = "Converter returned null";
                return result;
            }

            // 4. Format converted state as JSON
            var jsonFormatter = new JsonFormatter(JsonFormatter.Settings.Default);
            result.ConvertedStateJson = jsonFormatter.Format(newState);
            result.ConvertedStateType = newState.GetType().FullName;

            // 5. Calculate new Agent ID
            var newAgentId = ConvertAgentId(record.Id, collectionTypeName);
            var targetAgentTypeName = MapAgentTypeName(collectionTypeName);
            if (targetAgentTypeName != ExtractShortTypeName(collectionTypeName) && newAgentId.Contains(':'))
            {
                var parts = newAgentId.Split(':', 2);
                if (parts.Length == 2)
                    newAgentId = $"{targetAgentTypeName}:{parts[1]}";
            }
            result.NewAgentId = newAgentId;
            result.TargetAgentType = targetAgentTypeName;

            // 6. Check additional records (for converters that generate extra records)
            if (converter is UserBillingGrainStateConverter grainConverter && grainConverter.AdditionalPaymentRecords.Count > 0)
            {
                result.AdditionalRecords = grainConverter.AdditionalPaymentRecords
                    .Select(r => new AdditionalRecordInfo { AgentId = r.AgentId, StateType = r.State.GetType().Name })
                    .ToList();
            }
            else if (converter is UserBillingStateConverter billingConverter && billingConverter.AdditionalPaymentRecords.Count > 0)
            {
                result.AdditionalRecords = billingConverter.AdditionalPaymentRecords
                    .Select(r => new AdditionalRecordInfo { AgentId = r.AgentId, StateType = r.State.GetType().Name })
                    .ToList();
            }

            // 7. Optionally write to database
            if (writeToDb)
            {
                var recordsToWrite = new List<(string AgentId, IMessage State, string AgentTypeName)> 
                { 
                    (newAgentId, newState, targetAgentTypeName) 
                };
                
                // Also write AdditionalPaymentRecords if any
                if (converter is UserBillingGrainStateConverter grainConv)
                {
                    foreach (var (agentId, paymentState) in grainConv.AdditionalPaymentRecords)
                    {
                        recordsToWrite.Add((agentId, paymentState, "PaymentRecordGAgent"));
                    }
                }
                else if (converter is UserBillingStateConverter billingConv)
                {
                    foreach (var (agentId, paymentState) in billingConv.AdditionalPaymentRecords)
                    {
                        recordsToWrite.Add((agentId, paymentState, "PaymentRecordGAgent"));
                    }
                }
                
                var writeResult = await BulkWriteStateAsync(recordsToWrite, cancellationToken);
                result.WriteSuccess = writeResult.SuccessCount > 0;
                result.WriteMessage = $"Written {writeResult.SuccessCount} records to database (main + {recordsToWrite.Count - 1} payment records)";
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StateMigration] Error testing single record migration: {Id}", recordId);
            result.Error = ex.Message;
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Fetch a single record from old system by ID
    /// </summary>
    private async Task<ExportedRecord?> FetchSingleRecordAsync(
        string collectionTypeName,
        string recordId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build URL to fetch single record
            var url = $"{_options.OldSystemApiBaseUrl}/api/admin/export/grain" +
                $"?collection={Uri.EscapeDataString(collectionTypeName)}" +
                $"&id={Uri.EscapeDataString(recordId)}";

            _logger.LogInformation("[StateMigration] Fetching single record: {Url}", url);

            var response = await _httpClient.GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[StateMigration] API returned {StatusCode}: {Content}", 
                    response.StatusCode, content);
                return null;
            }

            var doc = JsonDocument.Parse(content);
            
            // API returns: { "code": "20000", "data": { "records": [...] } }
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                // Check if data has "records" array (standard API response)
                if (dataElement.TryGetProperty("records", out var recordsElement) && 
                    recordsElement.ValueKind == JsonValueKind.Array)
                {
                    var records = JsonSerializer.Deserialize<List<ExportedRecord>>(
                        recordsElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return records?.FirstOrDefault();
                }
                
                // Fallback: data is directly an array
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    var records = JsonSerializer.Deserialize<List<ExportedRecord>>(
                        dataElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return records?.FirstOrDefault();
                }
                
                // Fallback: data is a single object
                if (dataElement.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<ExportedRecord>(
                        dataElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StateMigration] Failed to fetch single record: {Id}", recordId);
            throw;
        }
    }

    /// <summary>
    /// Verify migrated data by querying Agent via Service layer
    /// </summary>
    public async Task<VerifyResult> VerifyAgentStateAsync(
        string agentType,
        string userId,
        CancellationToken cancellationToken)
    {
        var result = new VerifyResult
        {
            AgentType = agentType,
            UserId = userId
        };

        try
        {
            if (!Guid.TryParse(userId, out var userGuid))
            {
                result.Found = false;
                result.Message = $"Invalid userId format: {userId}";
                return result;
            }

            // Query Agent based on type
            switch (agentType.ToLowerInvariant())
            {
                case "invitationgagent":
                case "invitation":
                    if (_invitationService == null)
                    {
                        result.Found = false;
                        result.Message = "IInvitationService not available";
                        return result;
                    }
                    var invitationInfo = await _invitationService.GetInvitationInfoAsync(userGuid);
                    result.Found = true;
                    result.AgentData = new
                    {
                        InviteCode = invitationInfo.InviteCode,
                        TotalInvites = invitationInfo.TotalInvites,
                        ValidInvites = invitationInfo.ValidInvites,
                        TotalCreditsEarned = invitationInfo.TotalCreditsEarned
                    };
                    result.Message = "Agent state retrieved successfully";
                    break;

                case "userquotagagent":
                case "userquota":
                    if (_actorFactory == null)
                    {
                        result.Found = false;
                        result.Message = "IGAgentActorFactory not available";
                        return result;
                    }
                    var quotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userGuid.ToString());
                    var quotaGAgent = quotaActor.As<IUserQuotaGAgent>();
                    var quotaState = await quotaGAgent.GetUserQuotaStateAsync();
                    result.Found = true;
                    result.AgentData = new
                    {
                        Credits = quotaState.Credits,
                        HasInitialCredits = quotaState.HasInitialCredits,
                        HasShownInitialCreditsToast = quotaState.HasShownInitialCreditsToast,
                        Subscription = quotaState.Subscription != null ? new { quotaState.Subscription.IsActive, quotaState.Subscription.PlanType } : null,
                        UltimateSubscription = quotaState.UltimateSubscription != null ? new { quotaState.UltimateSubscription.IsActive, quotaState.UltimateSubscription.PlanType } : null
                    };
                    result.Message = "Agent state retrieved successfully";
                    break;

                case "userbillinggagent":
                case "userbilling":
                case "paymentindexgagent":
                case "paymentindex":
                    if (_actorFactory == null)
                    {
                        result.Found = false;
                        result.Message = "IGAgentActorFactory not available";
                        return result;
                    }
                    var billingActor = await _actorFactory.CreateGAgentActorAsync<PaymentIndexGAgent>(userGuid.ToString());
                    var billingGAgent = billingActor.As<IPaymentIndexGAgent>();
                    var subscriptions = await billingGAgent.GetAllSubscriptionsAsync();
                    var paymentCount = await billingGAgent.GetTotalPaymentCountAsync();
                    result.Found = true;
                    result.AgentData = new
                    {
                        TotalPaymentCount = paymentCount,
                        SubscriptionCount = subscriptions?.Subscriptions?.Count ?? 0,
                        Subscriptions = subscriptions?.Subscriptions?.Take(5).Select(s => new { s.PaymentId, s.ProductName, s.BusinessType, s.PeriodEnd })
                    };
                    result.Message = "Agent state retrieved successfully";
                    break;

                case "chatgagentmanager":
                case "chatmanager":
                    if (_actorFactory == null)
                    {
                        result.Found = false;
                        result.Message = "IGAgentActorFactory not available";
                        return result;
                    }
                    var chatActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userGuid.ToString());
                    var chatManager = chatActor.As<IChatManagerGAgent>();
                    var sessions = await chatManager.GetSessionListAsync();
                    result.Found = true;
                    result.AgentData = new
                    {
                        SessionCount = sessions?.Sessions?.Count ?? 0,
                        Sessions = sessions?.Sessions?.Take(5).Select(s => new { s.SessionId, s.Title, s.CreateAt })
                    };
                    result.Message = "Agent state retrieved successfully";
                    break;

                default:
                    result.Found = false;
                    result.Message = $"Unsupported agent type: {agentType}. Supported: InvitationGAgent, UserQuotaGAgent, PaymentIndexGAgent, ChatGAgentManager";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Found = false;
            result.Message = $"Error querying agent: {ex.Message}";
            _logger.LogError(ex, "[StateMigration] Error verifying agent state: {AgentType}/{UserId}", agentType, userId);
        }

        return result;
    }

    #endregion
}

/// <summary>
/// ES sync result
/// </summary>
public class EsSyncResult
{
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public string? Error { get; set; }
    public List<EsCollectionSyncResult> CollectionResults { get; set; } = new();
}

/// <summary>
/// ES sync result per collection
/// </summary>
public class EsCollectionSyncResult
{
    public string CollectionName { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public string? Error { get; set; }
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
        // IMPORTANT: If any data exists (UserId, AppRatings), mark as initialized
        // to prevent re-initialization on activation which would overwrite UserId
        var hasAnyData = oldState.ContainsKey("UserId") || oldState.ContainsKey("AppRatings");
        if (hasAnyData)
        {
            newState.IsInitialized = true;
        }
        else if (oldState.TryGetValue("IsInitialized", out var isInitObj))
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
/// Single record migration test result
/// </summary>
public class SingleRecordMigrationResult
{
    public string CollectionTypeName { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    
    public Dictionary<string, object?>? OriginalState { get; set; }
    public string? OriginalStateJson { get; set; }
    
    public string? ConverterUsed { get; set; }
    public string? ConvertedStateJson { get; set; }
    public string? ConvertedStateType { get; set; }
    
    public string? NewAgentId { get; set; }
    public string? TargetAgentType { get; set; }
    
    public List<AdditionalRecordInfo>? AdditionalRecords { get; set; }
    
    public bool? WriteSuccess { get; set; }
    public string? WriteMessage { get; set; }
}

public class AdditionalRecordInfo
{
    public string AgentId { get; set; } = string.Empty;
    public string StateType { get; set; } = string.Empty;
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

public class VerifyResult
{
    public string AgentType { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public bool Found { get; set; }
    public string? Message { get; set; }
    public object? AgentData { get; set; }
}
