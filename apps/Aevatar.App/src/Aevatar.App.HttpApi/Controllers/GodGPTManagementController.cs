using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Security;
using System.Threading.Tasks;
using Aevatar.Application.Contracts.Services;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Dtos;
using Aevatar.Options;
using Aevatar.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.Controllers;

/// <summary>
/// Weekly user feedback report controller
/// </summary>
[ApiController]
[Route("api/godgpt/management")]
[Authorize]
public class GodGPTManagementController : AbpControllerBase
{
    private readonly ILogger<GodGPTManagementController> _logger;
    private readonly IUserFeedbackService _userFeedbackService;
    private readonly IGodGPTService _godGptService;
    private readonly WeeklyFeedbackReportOptions _options;

    public GodGPTManagementController(
        ILogger<GodGPTManagementController> logger, 
        IUserFeedbackService userFeedbackService,
        IGodGPTService godGptService, IOptionsSnapshot<WeeklyFeedbackReportOptions> options)
    {
        _userFeedbackService = userFeedbackService;
        _logger = logger;
        _godGptService = godGptService;
        _options = options.Value;
    }

    /// <summary>
    /// Manually trigger weekly feedback report generation and sending
    /// </summary>
    /// <returns></returns>
    [HttpPost("feedback/report")]
    public async Task<IActionResult> TriggerWeeklyReportAsync(TriggerWeeklyReportInput input)
    {
        try
        {
            _logger.LogInformation("Manual trigger for weekly feedback report requested");

            await CheckUserIsManager();
            
            // Calculate time range
            var startTime = input.StartDate ?? default;
            var endTime = input.EndDate ?? default;
            if (startTime == default || endTime == default)
            {
                // Use default time range: last Sunday 12:00 PM to current Sunday 12:00 PM
                var (defaultStartTime, defaultEndTime) = CalculateWeeklyTimeRange();
                startTime = defaultStartTime;
                endTime = defaultEndTime;
            }

            _logger.LogInformation("Generating feedback report for period: {StartTime} to {EndTime}", 
                startTime, endTime);

            // Query feedback data
            var feedbackRecords = await _userFeedbackService.GetFeedbackDataAsync(startTime, endTime);
            
            if (feedbackRecords.Count == 0)
            {
                return Ok(new { 
                    Success = true, 
                    Message = "No feedback data found for the specified period",
                    StartDate = startTime.ToString("yyyy-MM-dd"),
                    EndDate = endTime.ToString("yyyy-MM-dd"),
                    FeedbackCount = 0
                });
            }

            // Generate CSV content
            var csvContent = _userFeedbackService.GenerateCsvContent(feedbackRecords);

            // Send email (if recipient emails provided)
            if (!string.IsNullOrEmpty(input.RecipientEmails))
            {
                await _userFeedbackService.SendWeeklyFeedbackReportAsync(
                    input.RecipientEmails, 
                    csvContent, 
                    startTime, 
                    endTime, 
                    feedbackRecords.Count);

                return Ok(new { 
                    Success = true, 
                    Message = "Weekly feedback report generated and sent successfully",
                    RecipientEmails = input.RecipientEmails,
                    StartDate = startTime.ToString("yyyy-MM-dd"),
                    EndDate = endTime.ToString("yyyy-MM-dd"),
                    FeedbackCount = feedbackRecords.Count
                });
            }
            else
            {
                return Ok(new { 
                    Success = true, 
                    Message = "Weekly feedback report generated successfully (no email sent)",
                    StartDate = startTime.ToString("yyyy-MM-dd"),
                    EndDate = endTime.ToString("yyyy-MM-dd"),
                    FeedbackCount = feedbackRecords.Count,
                    CsvPreview = csvContent.Length > 500 ? csvContent.Substring(0, 500) + "..." : csvContent
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger weekly feedback report");
            return StatusCode(500, new { 
                Success = false, 
                Message = "Failed to generate weekly feedback report",
                Error = ex.Message 
            });
        }
    }
    
    [HttpGet("batch-info/{batchId}")]
    public async Task<BatchInfoDto> GetBatchInfoAsync(string batchId)
    {
        await CheckUserIsManager();
        
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var batchInfoDto = await _godGptService.GetBatchInfoAsync(batchId);
        _logger.LogDebug("[GodGPTInvitationController][GetBatchInfoAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return batchInfoDto;
    }

    /// <summary>
    /// Calculate weekly time range based on configured execution day and hour
    /// </summary>
    private (DateTime startTime, DateTime endTime) CalculateWeeklyTimeRange()
    {
        var now = DateTime.UtcNow;
        var executionDayOfWeek = _options.ExecutionDayOfWeek;
        var executionHour = _options.ExecutionHour;
        
        // Calculate days from configured execution day
        // DayOfWeek enum: Sunday=0, Monday=1, ..., Saturday=6
        var daysFromExecutionDay = ((int)now.DayOfWeek - (int)executionDayOfWeek + 7) % 7;
        var currentExecutionDay = now.Date.AddDays(-daysFromExecutionDay).AddHours(executionHour);
        
        // If current time hasn't reached this week's execution time, use last week's time range
        if (now < currentExecutionDay)
        {
            currentExecutionDay = currentExecutionDay.AddDays(-7);
        }

        var startTime = currentExecutionDay.AddDays(-7); // Last execution day
        var endTime = currentExecutionDay; // Current execution day

        return (startTime, endTime);
    }
    
    private async Task CheckUserIsManager()
    {
        var currentUserId = (Guid)CurrentUser.Id!;
        if (!await _godGptService.CheckIsManager(currentUserId))
        {
            _logger.LogInformation($"User is not manager {currentUserId}");
            throw new SecurityException($"User is not manager {currentUserId}");
        }
    }
}