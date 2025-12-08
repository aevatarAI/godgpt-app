using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// User feedback service interface
/// </summary>
public interface IUserFeedbackService
{
    /// <summary>
    /// Query user feedback data within specified time range
    /// </summary>
    /// <param name="startTime">Start time</param>
    /// <param name="endTime">End time</param>
    /// <returns>List of user feedback records</returns>
    Task<List<FeedbackCsvRecordDto>> GetFeedbackDataAsync(DateTime startTime, DateTime endTime);

    /// <summary>
    /// Generate CSV file content
    /// </summary>
    /// <param name="feedbackRecords">List of feedback records</param>
    /// <returns>CSV file content</returns>
    string GenerateCsvContent(List<FeedbackCsvRecordDto> feedbackRecords);

    /// <summary>
    /// Send weekly feedback report email
    /// </summary>
    /// <param name="recipientEmails">Recipient email addresses (separated by semicolon)</param>
    /// <param name="csvContent">CSV file content</param>
    /// <param name="startDate">Report start date</param>
    /// <param name="endDate">Report end date</param>
    /// <param name="feedbackCount">Feedback count</param>
    /// <returns></returns>
    Task SendWeeklyFeedbackReportAsync(string recipientEmails, string csvContent, DateTime startDate, DateTime endDate, int feedbackCount);

    /// <summary>
    /// Send weekly feedback report email without CSV attachment
    /// </summary>
    /// <param name="recipientEmails">Recipient email addresses (separated by semicolon)</param>
    /// <param name="startDate">Report start date</param>
    /// <param name="endDate">Report end date</param>
    /// <param name="feedbackCount">Feedback count</param>
    /// <returns></returns>
    Task SendWeeklyFeedbackReportWithoutAttachmentAsync(string recipientEmails, DateTime startDate, DateTime endDate, int feedbackCount);
}
