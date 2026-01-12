using Microsoft.Extensions.Logging;
using MigrationTool.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace MigrationTool.Readers.UserStatistics;

/// <summary>
/// Reads old UserStatistics state from Orleans Grain Storage (MongoDB)
/// </summary>
public class UserStatisticsOldReader : IOldStateReader<OldUserStatisticsState>
{
    private readonly IMongoCollection<BsonDocument> _grainStateCollection;
    private readonly ILogger<UserStatisticsOldReader> _logger;
    
    // Orleans grain type name pattern - adjust based on your actual grain type
    private const string GrainTypePattern = "UserStatisticsGAgent";
    
    public UserStatisticsOldReader(
        IMongoDatabase database,
        string collectionName,
        ILogger<UserStatisticsOldReader> logger)
    {
        _grainStateCollection = database.GetCollection<BsonDocument>(collectionName);
        _logger = logger;
    }

    /// <summary>
    /// Read a single agent's state by ID
    /// </summary>
    public async Task<OldUserStatisticsState?> ReadAsync(string agentId)
    {
        try
        {
            // Try different filter patterns based on Orleans storage format
            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", agentId),
                Builders<BsonDocument>.Filter.Eq("GrainId", agentId),
                Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression(agentId))
            );
            
            var doc = await _grainStateCollection.Find(filter).FirstOrDefaultAsync();
            if (doc == null) 
            {
                _logger.LogDebug("No document found for AgentId: {AgentId}", agentId);
                return null;
            }

            return DeserializeState(doc, agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read UserStatistics state for {AgentId}", agentId);
            return null;
        }
    }

    /// <summary>
    /// Read all UserStatistics states from the collection
    /// </summary>
    public async IAsyncEnumerable<(string AgentId, OldUserStatisticsState State)> ReadAllAsync()
    {
        // Filter for UserStatistics grain type
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Regex("GrainType", new BsonRegularExpression(GrainTypePattern, "i")),
            Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression(GrainTypePattern, "i"))
        );
        
        using var cursor = await _grainStateCollection
            .Find(filter)
            .ToCursorAsync();
        
        while (await cursor.MoveNextAsync())
        {
            foreach (var doc in cursor.Current)
            {
                string agentId;
                try
                {
                    agentId = ExtractAgentId(doc);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract AgentId from document");
                    continue;
                }
                
                var state = DeserializeState(doc, agentId);
                if (state != null)
                {
                    yield return (agentId, state);
                }
            }
        }
    }

    /// <summary>
    /// Get total count of UserStatistics agents
    /// </summary>
    public async Task<int> GetCountAsync()
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Regex("GrainType", new BsonRegularExpression(GrainTypePattern, "i")),
            Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression(GrainTypePattern, "i"))
        );
        
        return (int)await _grainStateCollection.CountDocumentsAsync(filter);
    }

    /// <summary>
    /// Get a sample of agent IDs for verification
    /// </summary>
    public async Task<List<string>> GetSampleIdsAsync(int count)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Regex("GrainType", new BsonRegularExpression(GrainTypePattern, "i")),
            Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression(GrainTypePattern, "i"))
        );
        
        var docs = await _grainStateCollection
            .Find(filter)
            .Limit(count)
            .ToListAsync();
            
        return docs.Select(ExtractAgentId).ToList();
    }

    /// <summary>
    /// Extract AgentId from document
    /// </summary>
    private string ExtractAgentId(BsonDocument doc)
    {
        // Try different field names based on Orleans storage format
        if (doc.Contains("GrainId"))
        {
            return doc["GrainId"].AsString;
        }
        
        if (doc.Contains("_id"))
        {
            var id = doc["_id"];
            if (id.IsString)
            {
                // Extract Guid part from Orleans grain key format
                var idStr = id.AsString;
                // Orleans format might be: "GrainType/GrainKey" or just "GrainKey"
                var parts = idStr.Split('/');
                var guidPart = parts.Last();
                
                // Validate it's a valid Guid
                if (Guid.TryParse(guidPart, out _))
                {
                    return guidPart;
                }
                
                return idStr;
            }
        }
        
        throw new InvalidOperationException("Cannot extract AgentId from document");
    }

    /// <summary>
    /// Deserialize state from BSON document
    /// </summary>
    private OldUserStatisticsState? DeserializeState(BsonDocument doc, string agentId)
    {
        try
        {
            // Orleans stores state in different ways based on configuration
            BsonValue? stateValue = null;
            
            // Try common field names
            if (doc.Contains("State"))
            {
                stateValue = doc["State"];
            }
            else if (doc.Contains("state"))
            {
                stateValue = doc["state"];
            }
            else if (doc.Contains("Data"))
            {
                stateValue = doc["Data"];
            }
            
            if (stateValue == null)
            {
                // Maybe the document IS the state
                stateValue = doc;
            }
            
            // Try to deserialize
            if (stateValue.IsBsonDocument)
            {
                var json = stateValue.ToJson();
                return JsonConvert.DeserializeObject<OldUserStatisticsState>(json);
            }
            else if (stateValue.IsString)
            {
                return JsonConvert.DeserializeObject<OldUserStatisticsState>(stateValue.AsString);
            }
            else if (stateValue.IsBsonBinaryData)
            {
                // Binary serialized - need Orleans deserializer
                _logger.LogWarning("Binary state format not supported for {AgentId}", agentId);
                return null;
            }
            
            _logger.LogWarning("Unknown state format for {AgentId}", agentId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize state for {AgentId}", agentId);
            return null;
        }
    }
}
