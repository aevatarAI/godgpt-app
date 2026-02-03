using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.App.HttpApi.Host.BackgroundJobs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.HttpApi.Host.Controllers;

/// <summary>
/// API controller for triggering State Migration job asynchronously via Hangfire
/// </summary>
[ApiController]
[Route("api/admin/migration")]
[AllowAnonymous] // Temporarily enabled for local testing
public class StateMigrationController : AbpControllerBase
{
    private readonly StateMigrationJob _migrationJob;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly StateMigrationOptions _options;

    public StateMigrationController(
        StateMigrationJob migrationJob,
        IBackgroundJobClient backgroundJobClient,
        IOptions<StateMigrationOptions> options)
    {
        _migrationJob = migrationJob;
        _backgroundJobClient = backgroundJobClient;
        _options = options.Value;
    }

    /// <summary>
    /// Trigger migration job asynchronously via Hangfire
    /// POST /api/admin/migration/execute-async
    /// </summary>
    [HttpPost("execute-async")]
    public IActionResult ExecuteAsyncAsync([FromBody] MigrationRequest? request)
    {
        var collectionTypes = request?.CollectionTypes;
        var jobId = _backgroundJobClient.Enqueue<StateMigrationJob>(
            job => job.ExecuteAsync(collectionTypes, default));

        return Ok(new { JobId = jobId, Message = "Migration job queued" });
    }

    /// <summary>
    /// Get migration job status
    /// GET /api/admin/migration/status/{jobId}
    /// </summary>
    [HttpGet("status/{jobId}")]
    public IActionResult GetStatus(string jobId)
    {
        // Use Hangfire's Monitoring API
        using var connection = JobStorage.Current.GetConnection();
        var jobData = connection.GetJobData(jobId);
        
        if (jobData == null)
        {
            return NotFound(new { Error = "Job not found" });
        }

        return Ok(new
        {
            JobId = jobId,
            State = jobData.State,
            CreatedAt = jobData.CreatedAt,
            // Add more status info as needed
        });
    }

    /// <summary>
    /// Sync MongoDB state data to Elasticsearch.
    /// POST /api/admin/migration/sync-es
    /// </summary>
    /// <remarks>
    /// Reads state data from MongoDB (agent_states_{collectionName}) and indexes to Elasticsearch.
    /// Useful for migrated data that bypassed the normal Projector flow.
    /// 
    /// Example request:
    /// {
    ///   "collectionNames": ["UserDeviceState", "PayRecordState"]
    /// }
    /// </remarks>
    [HttpPost("sync-es")]
    public IActionResult SyncToElasticsearch([FromBody] EsSyncRequest request)
    {
        if (request.CollectionNames == null || request.CollectionNames.Count == 0)
        {
            return BadRequest(new { Error = "CollectionNames is required" });
        }

        var jobId = _backgroundJobClient.Enqueue<StateMigrationJob>(
            job => job.SyncToElasticsearchAsync(request.CollectionNames, default));

        return Ok(new { JobId = jobId, Message = "ES sync job queued", Collections = request.CollectionNames });
    }

    /// <summary>
    /// Sync MongoDB state data to Elasticsearch synchronously (for small datasets).
    /// POST /api/admin/migration/sync-es-sync
    /// </summary>
    [HttpPost("sync-es-sync")]
    public async Task<IActionResult> SyncToElasticsearchSync(
        [FromBody] EsSyncRequest request, 
        CancellationToken cancellationToken)
    {
        if (request.CollectionNames == null || request.CollectionNames.Count == 0)
        {
            return BadRequest(new { Error = "CollectionNames is required" });
        }

        var result = await _migrationJob.SyncToElasticsearchAsync(request.CollectionNames, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Test migration of a single record by ID.
    /// POST /api/admin/migration/test-single
    /// </summary>
    /// <remarks>
    /// Fetches a single record from old system, converts it using the appropriate converter,
    /// and returns both original and converted data for comparison.
    /// 
    /// Example request:
    /// {
    ///   "collectionTypeName": "InvitationGAgent",
    ///   "recordId": "9bcf411b-f21e-f1d9-10cc-3a1aa2a71eb7",
    ///   "writeToDb": false
    /// }
    /// </remarks>
    [HttpPost("test-single")]
    public async Task<IActionResult> TestSingleRecordMigration(
        [FromBody] TestSingleRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CollectionTypeName))
        {
            return BadRequest(new { Error = "CollectionTypeName is required" });
        }

        if (string.IsNullOrWhiteSpace(request.RecordId))
        {
            return BadRequest(new { Error = "RecordId is required" });
        }

        var result = await _migrationJob.TestSingleRecordMigrationAsync(
            request.CollectionTypeName,
            request.RecordId,
            request.WriteToDb,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Verify migrated data by querying Agent directly via Orleans
    /// GET /api/admin/migration/verify?agentType=InvitationGAgent&userId=guid
    /// </summary>
    [HttpGet("verify")]
    public async Task<IActionResult> VerifyMigratedData(
        [FromQuery] string agentType,
        [FromQuery] string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentType) || string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { Error = "agentType and userId are required" });
        }

        var result = await _migrationJob.VerifyAgentStateAsync(agentType, userId, cancellationToken);
        return Ok(result);
    }
}

public class TestSingleRequest
{
    /// <summary>
    /// Collection type name (e.g., "InvitationGAgent", "InviteCodeGAgent")
    /// </summary>
    public string CollectionTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Record ID (GUID format, e.g., "9bcf411b-f21e-f1d9-10cc-3a1aa2a71eb7")
    /// </summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// Whether to write the converted record to database. Default: false (dry run)
    /// </summary>
    public bool WriteToDb { get; set; } = false;
}

public class MigrationRequest
{
    public List<string>? CollectionTypes { get; set; }
}

public class EsSyncRequest
{
    /// <summary>
    /// Collection names to sync (e.g., "UserDeviceState", "PayRecordState")
    /// These correspond to MongoDB collections: agent_states_{collectionName}
    /// </summary>
    public List<string> CollectionNames { get; set; } = new();
}
