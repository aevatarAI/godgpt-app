using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// User feedback query response DTO
/// </summary>
public class UserFeedbackQueryResponseDto
{
    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("data")]
    public UserFeedbackDataDto? Data { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// User feedback data DTO
/// </summary>
public class UserFeedbackDataDto
{
    [JsonProperty("totalCount")]
    public int TotalCount { get; set; }

    [JsonProperty("items")]
    public List<UserFeedbackItemDto> Items { get; set; } = new();
}

/// <summary>
/// User feedback item DTO
/// </summary>
public class UserFeedbackItemDto
{
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("lastFeedbackTime")]
    public DateTime LastFeedbackTime { get; set; }

    [JsonProperty("children")]
    public string Children { get; set; } = string.Empty;

    // [JsonProperty("archivedFeedbacks")]
    // public string ArchivedFeedbacks { get; set; } = string.Empty;

    [JsonProperty("feedbackCount")]
    public double FeedbackCount { get; set; }

    [JsonProperty("currentFeedback")]
    public string CurrentFeedback { get; set; } = string.Empty;

    [JsonProperty("ctime")]
    public DateTime Ctime { get; set; }

    [JsonProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonProperty("version")]
    public double Version { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Current feedback details DTO
/// </summary>
public class CurrentFeedbackDto
{
    [JsonProperty("feedbackId")]
    public string FeedbackId { get; set; } = string.Empty;

    [JsonProperty("feedbackType")]
    public string FeedbackType { get; set; } = string.Empty;

    [JsonProperty("reasons")]
    public List<int> Reasons { get; set; } = new();

    [JsonProperty("response")]
    public string Response { get; set; } = string.Empty;

    [JsonProperty("contactRequested")]
    public bool ContactRequested { get; set; }

    [JsonProperty("email")]
    public string Email { get; set; } = string.Empty;

    [JsonProperty("submittedAt")]
    public DateTime SubmittedAt { get; set; }

    [JsonProperty("reasonTextsEnglish")]
    public List<string> ReasonTextsEnglish { get; set; } = new();
}

/// <summary>
/// Feedback record DTO for CSV export
/// </summary>
public class FeedbackCsvRecordDto
{
    /// <summary>
    /// Feedback time
    /// </summary>
    public DateTime FeedbackTime { get; set; }

    /// <summary>
    /// Action type (Cancel/Change)
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Reasons (selected reasons)
    /// </summary>
    public string Reasons { get; set; } = string.Empty;

    /// <summary>
    /// User input text content
    /// </summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>
    /// Whether contact checkbox is checked
    /// </summary>
    public bool CheckForContact { get; set; }

    /// <summary>
    /// Email address
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
