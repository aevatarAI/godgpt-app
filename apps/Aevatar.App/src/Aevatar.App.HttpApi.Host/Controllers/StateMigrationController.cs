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
}

public class MigrationRequest
{
    public List<string>? CollectionTypes { get; set; }
}
