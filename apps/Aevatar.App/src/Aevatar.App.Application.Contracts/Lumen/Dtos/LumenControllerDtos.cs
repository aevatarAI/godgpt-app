using System;
using System.Collections.Generic;
using Aevatar.Agents.Lumen.Protos;

namespace Aevatar.App.Lumen.Dtos;

/// <summary>
/// API request for registering/updating user profile (Controller layer)
/// </summary>
public class RegisterUserProfileApiRequest
{
    public string FullName { get; set; } = string.Empty;
    public GenderEnum Gender { get; set; }
    public DateOnly BirthDate { get; set; }
    public TimeOnly BirthTime { get; set; }
    public string BirthCity { get; set; } = string.Empty;
    public string? LatLong { get; set; }
    public MbtiTypeEnum? MbtiType { get; set; }
    public RelationshipStatusEnum? RelationshipStatus { get; set; }
    public string? Interests { get; set; }
    public CalendarTypeEnum? CalendarType { get; set; }
    public string? CurrentResidence { get; set; }
    public string? Email { get; set; }
    public string? Occupation { get; set; }
    public string? Icon { get; set; }
    public string? CurrentTimeZone { get; set; }
    public List<InterestEnum>? InterestsList { get; set; }
}

/// <summary>
/// API request for updating timezone (Controller layer)
/// </summary>
public class UpdateTimeZoneApiRequest
{
    public string TimeZoneId { get; set; } = string.Empty;
}

/// <summary>
/// API request for triggering prediction generation (Controller layer)
/// </summary>
public class TriggerGenerationRequest
{
    public List<PredictionType> Types { get; set; } = new();
}

/// <summary>
/// API request for submitting feedback (Controller layer)
/// </summary>
public class SubmitFeedbackApiRequest
{
    public string? PredictionId { get; set; }
    public string PredictionMethod { get; set; } = string.Empty;
    public int Rating { get; set; }
    public List<string> FeedbackTypes { get; set; } = new();
    public string? Comment { get; set; }
    public string? Email { get; set; }
    public bool AgreeToContact { get; set; }
}

/// <summary>
/// API request for updating method rating (Controller layer)
/// </summary>
public class UpdateMethodRatingApiRequest
{
    public string PredictionId { get; set; } = string.Empty;
    public string PredictionMethod { get; set; } = string.Empty;
    public int Rating { get; set; }
}

/// <summary>
/// API request for toggling favourite (Controller layer)
/// </summary>
public class ToggleFavouriteApiRequest
{
    public string PredictionId { get; set; } = string.Empty;
    public PredictionType PredictionType { get; set; }
    public bool IsFavourite { get; set; }
}

/// <summary>
/// API request for getting prediction by date (Controller layer)
/// </summary>
public class GetPredictionByDateApiRequest
{
    public DateOnly Date { get; set; }
}

/// <summary>
/// API request for getting recent predictions (Controller layer)
/// </summary>
public class GetRecentPredictionsApiRequest
{
    public string Date { get; set; } = string.Empty;
}

/// <summary>
/// API response for Lumen user profile (Controller layer)
/// </summary>
public class LumenUserProfileApiDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public GenderEnum Gender { get; set; }
    public DateValue BirthDate { get; set; } = new();
    public TimeValue? BirthTime { get; set; }
    public string BirthCity { get; set; } = string.Empty;
    public string LatLong { get; set; } = string.Empty;
    public CalendarTypeEnum? CalendarType { get; set; }
    public long CreatedAt { get; set; }
    public string? CurrentResidence { get; set; }
    public long UpdatedAt { get; set; }
    public Dictionary<string, string> WelcomeNote { get; set; } = new();
    public string ZodiacSign { get; set; } = string.Empty;
    public ZodiacSignEnum ZodiacSignEnum { get; set; }
    public string ChineseZodiac { get; set; } = string.Empty;
    public ChineseZodiacEnum ChineseZodiacEnum { get; set; }
    public string? Occupation { get; set; }
    public MbtiTypeEnum? MbtiType { get; set; }
    public RelationshipStatusEnum? RelationshipStatus { get; set; }
    public string? Interests { get; set; }
    public List<InterestEnum> InterestsList { get; set; } = new();
    public string? Email { get; set; }
    public string? Icon { get; set; }
    public string? CurrentTimeZone { get; set; }
    public string CurrentLanguage { get; set; } = string.Empty;
    public string? LatLongInferred { get; set; }
    public string? InferredFromCity { get; set; }
}
