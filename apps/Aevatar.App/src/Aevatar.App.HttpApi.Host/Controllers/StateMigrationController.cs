using System.Collections.Generic;
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
/// API controller for triggering State Migration job
/// </summary>
[ApiController]
[Route("api/admin/migration")]
// [Authorize] // Temporarily disabled for testing
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
    /// Trigger migration job synchronously (for small datasets)
    /// POST /api/admin/migration/execute
    /// </summary>
    [HttpPost("execute")]
    public async Task<MigrationResult> ExecuteAsync([FromBody] MigrationRequest? request)
    {
        var result = await _migrationJob.ExecuteAsync(request?.CollectionTypes);
        return result;
    }

    /// <summary>
    /// Trigger migration job asynchronously (for large datasets)
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
}

public class MigrationRequest
{
    public List<string>? CollectionTypes { get; set; }
}
