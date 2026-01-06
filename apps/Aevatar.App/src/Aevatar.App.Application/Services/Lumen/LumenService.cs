using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Lumen.Favourite;
using Aevatar.Agents.Lumen.Feedback;
using Aevatar.Agents.Lumen.History;
using Aevatar.Agents.Lumen.Prediction;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.Agents.Lumen.UserProfile;
using Aevatar.App.Application.Contracts.BlobStorings;
using Aevatar.App.Lumen;
using Aevatar.App.Lumen.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Application.Services;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;

namespace Aevatar.App.Services.Lumen;

/// <summary>
/// Lumen Service implementation - orchestrates Lumen GAgents
/// </summary>
public partial class LumenService : ApplicationService, ILumenService, ITransientDependency
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<LumenService> _logger;
    private readonly IBlobContainer _blobContainer;
    private readonly BlobStoringOptions _blobStoringOptions;

    public LumenService(
        IGAgentActorFactory actorFactory,
        ILogger<LumenService> logger,
        IBlobContainer blobContainer,
        IOptionsSnapshot<BlobStoringOptions> blobStoringOptions)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _blobContainer = blobContainer;
        _blobStoringOptions = blobStoringOptions.Value;
    }

    #region Helper Methods

    /// <summary>
    /// Convert string to Guid deterministically
    /// </summary>
    private static Guid StringToGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        return new Guid(hashBytes);
    }

    /// <summary>
    /// Get user's local date based on their timezone
    /// </summary>
    private DateOnly GetUserLocalDate(string? timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        try
        {
            var userTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var userNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, userTimeZone);
            return DateOnly.FromDateTime(userNow);
        }
        catch (TimeZoneNotFoundException ex)
        {
            _logger.LogWarning(ex, "[LumenService] Invalid timezone: {TimeZoneId}, falling back to UTC", timeZoneId);
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Get Lumen User Profile GAgent
    /// </summary>
    private async Task<ILumenUserProfileGAgent> GetUserProfileAgentAsync(string userId)
    {
        var grainId = StringToGuid(userId);
        var actor = await _actorFactory.CreateGAgentActorAsync<LumenUserProfileGAgent>(grainId.ToString());
        return actor.As<ILumenUserProfileGAgent>();
    }

    /// <summary>
    /// Get Lumen Prediction GAgent
    /// </summary>
    private async Task<ILumenPredictionGAgent> GetPredictionAgentAsync(string userId, PredictionType type)
    {
        var suffix = type switch
        {
            PredictionType.PredictionDaily => "daily",
            PredictionType.PredictionYearly => "yearly",
            PredictionType.PredictionLifetime => "lifetime",
            _ => throw new ArgumentException($"Invalid prediction type: {type}")
        };
        
        var grainId = StringToGuid($"{userId}_{suffix}");
        var actor = await _actorFactory.CreateGAgentActorAsync<LumenPredictionGAgent>(grainId.ToString());
        return actor.As<ILumenPredictionGAgent>();
    }

    /// <summary>
    /// Get Lumen Prediction History GAgent
    /// </summary>
    private async Task<ILumenPredictionHistoryGAgent> GetHistoryAgentAsync(string userId)
    {
        var grainId = StringToGuid($"{userId}_history");
        var actor = await _actorFactory.CreateGAgentActorAsync<LumenPredictionHistoryGAgent>(grainId.ToString());
        return actor.As<ILumenPredictionHistoryGAgent>();
    }

    /// <summary>
    /// Get Lumen Daily/Yearly History GAgent
    /// </summary>
    private async Task<ILumenDailyYearlyHistoryGAgent> GetDailyYearlyHistoryAgentAsync(string userId)
    {
        var grainId = StringToGuid($"{userId}_daily_yearly_history");
        var actor = await _actorFactory.CreateGAgentActorAsync<LumenDailyYearlyHistoryGAgent>(grainId.ToString());
        return actor.As<ILumenDailyYearlyHistoryGAgent>();
    }

    /// <summary>
    /// Get Lumen Feedback GAgent
    /// </summary>
    private async Task<ILumenFeedbackGAgent> GetFeedbackAgentAsync(string predictionId)
    {
        var grainId = StringToGuid($"feedback_{predictionId}");
        var actor = await _actorFactory.CreateGAgentActorAsync<LumenFeedbackGAgent>(grainId.ToString());
        return actor.As<ILumenFeedbackGAgent>();
    }

    /// <summary>
    /// Get Lumen Favourite GAgent
    /// </summary>
    private async Task<ILumenFavouriteGAgent> GetFavouriteAgentAsync(string userId)
    {
        var grainId = StringToGuid($"{userId}_favourites");
        var actor = await _actorFactory.CreateGAgentActorAsync<LumenFavouriteGAgent>(grainId.ToString());
        return actor.As<ILumenFavouriteGAgent>();
    }

    /// <summary>
    /// Build LumenUserDto from user profile
    /// </summary>
    private LumenUserDto BuildUserDto(LumenUserProfileDto profile)
    {
        return new LumenUserDto
        {
            UserId = profile.UserId,
            FullName = profile.FullName,
            BirthDate = profile.BirthDate,
            BirthTime = profile.BirthTime,
            BirthCity = profile.BirthCity,
            Gender = profile.Gender,
            CalendarType = profile.CalendarType,
            LatLong = profile.LatLong ?? string.Empty,
            LatLongInferred = profile.LatLongInferred ?? string.Empty
        };
    }

    #endregion
}
