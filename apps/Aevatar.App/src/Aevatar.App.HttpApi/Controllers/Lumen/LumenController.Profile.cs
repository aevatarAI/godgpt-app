using System;
using System.Linq;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen.Dtos;
using Aevatar.App.HttpApi.Controllers;

namespace Aevatar.App.Controllers.Lumen;

public partial class LumenController
{
    private static LumenUserProfileApiDto MapToUserProfileApiDto(LumenUserProfileDto profile)
    {
        return new LumenUserProfileApiDto
        {
            UserId = profile.UserId,
            FullName = profile.FullName,
            Gender = profile.Gender,
            BirthDate = profile.BirthDate,
            BirthTime = profile.BirthTime,
            BirthCity = profile.BirthCity,
            LatLong = profile.LatLong,
            CalendarType = profile.HasCalendarType ? profile.CalendarType : null,
            CreatedAt = DateTimeFormatHelper.ToIso8601String(profile.CreatedAt),
            CurrentResidence = NormalizeOptionalString(profile.CurrentResidence),
            UpdatedAt = DateTimeFormatHelper.ToIso8601String(profile.UpdatedAt),
            WelcomeNote = profile.WelcomeNote.ToDictionary(item => item.Key, item => item.Value),
            ZodiacSign = profile.ZodiacSign,
            ZodiacSignEnum = profile.ZodiacSignEnum,
            ChineseZodiac = profile.ChineseZodiac,
            ChineseZodiacEnum = profile.ChineseZodiacEnum,
            Occupation = NormalizeOptionalString(profile.Occupation),
            MbtiType = profile.HasMbtiType ? profile.MbtiType : null,
            RelationshipStatus = profile.HasRelationshipStatus ? profile.RelationshipStatus : null,
            Interests = NormalizeOptionalString(profile.Interests),
            InterestsList = profile.InterestsList.ToList(),
            Email = NormalizeOptionalString(profile.Email),
            Icon = NormalizeOptionalString(profile.Icon),
            CurrentTimeZone = NormalizeOptionalString(profile.CurrentTimeZone),
            CurrentLanguage = profile.CurrentLanguage,
            LatLongInferred = NormalizeOptionalString(profile.LatLongInferred),
            InferredFromCity = NormalizeOptionalString(profile.InferredFromCity)
        };
    }

    private static string? NormalizeOptionalString(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
