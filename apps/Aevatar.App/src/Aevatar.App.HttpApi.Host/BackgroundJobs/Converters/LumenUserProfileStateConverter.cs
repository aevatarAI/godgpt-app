using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// LumenUserProfile State converter
/// </summary>
public class LumenUserProfileStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new LumenUserProfileState();

        var newState = new LumenUserProfileState();

        // Basic fields
        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        if (oldState.TryGetValue("FullName", out var fullNameObj))
            newState.FullName = ConvertToString(fullNameObj);

        if (oldState.TryGetValue("Gender", out var genderObj))
            newState.Gender = ConvertToEnum<GenderEnum>(genderObj, GenderEnum.GenderUnspecified);

        // Birth date
        if (oldState.TryGetValue("BirthDate", out var birthDateObj))
        {
            var dateValue = ConvertToDateValue(birthDateObj);
            if (dateValue != null)
                newState.BirthDate = dateValue;
        }

        // Birth time (optional)
        if (oldState.TryGetValue("BirthTime", out var birthTimeObj))
        {
            var timeValue = ConvertToTimeValue(birthTimeObj);
            if (timeValue != null)
                newState.BirthTime = timeValue;
        }

        if (oldState.TryGetValue("BirthCity", out var birthCityObj))
            newState.BirthCity = ConvertToString(birthCityObj);

        if (oldState.TryGetValue("LatLong", out var latLongObj))
            newState.LatLong = ConvertToString(latLongObj);

        if (oldState.TryGetValue("MbtiType", out var mbtiTypeObj))
            newState.MbtiType = ConvertToEnum<MbtiTypeEnum>(mbtiTypeObj, MbtiTypeEnum.MbtiUnspecified);

        if (oldState.TryGetValue("RelationshipStatus", out var relationshipStatusObj))
            newState.RelationshipStatus = ConvertToEnum<RelationshipStatusEnum>(relationshipStatusObj, RelationshipStatusEnum.RelationshipUnspecified);

        if (oldState.TryGetValue("Interests", out var interestsObj))
            newState.Interests = ConvertToString(interestsObj);

        if (oldState.TryGetValue("CalendarType", out var calendarTypeObj))
            newState.CalendarType = ConvertToEnum<CalendarTypeEnum>(calendarTypeObj, CalendarTypeEnum.CalendarSolar);

        // Actions list
        if (oldState.TryGetValue("Actions", out var actionsObj))
        {
            if (actionsObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var action = ConvertToString(item);
                    if (!string.IsNullOrEmpty(action))
                        newState.Actions.Add(action);
                }
            }
        }

        if (oldState.TryGetValue("CurrentResidence", out var currentResidenceObj))
            newState.CurrentResidence = ConvertToString(currentResidenceObj);

        if (oldState.TryGetValue("Email", out var emailObj))
            newState.Email = ConvertToString(emailObj);

        if (oldState.TryGetValue("Occupation", out var occupationObj))
            newState.Occupation = ConvertToString(occupationObj);

        if (oldState.TryGetValue("Icon", out var iconObj))
            newState.Icon = ConvertToString(iconObj);

        if (oldState.TryGetValue("CurrentLanguage", out var currentLanguageObj))
            newState.CurrentLanguage = ConvertToString(currentLanguageObj) ?? "en";

        if (oldState.TryGetValue("CurrentTimeZone", out var currentTimeZoneObj))
            newState.CurrentTimeZone = ConvertToString(currentTimeZoneObj);

        if (oldState.TryGetValue("IsDeleted", out var isDeletedObj))
            newState.IsDeleted = ConvertToBool(isDeletedObj);

        // Timestamps
        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("UpdatedAt", out var updatedAtObj))
        {
            var dt = ConvertToDateTime(updatedAtObj);
            if (dt.HasValue)
                newState.UpdatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("LastActiveDate", out var lastActiveDateObj))
        {
            var dt = ConvertToDateTime(lastActiveDateObj);
            if (dt.HasValue)
                newState.LastActiveDate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("IsDailyReminderEnabled", out var isDailyReminderEnabledObj))
            newState.IsDailyReminderEnabled = ConvertToBool(isDailyReminderEnabledObj);

        return newState;
    }

    private static DateValue? ConvertToDateValue(object? obj)
    {
        if (obj == null) return null;
        
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var dateValue = new DateValue();
            if (je.TryGetProperty("Year", out var yearEl))
                dateValue.Year = ConvertToInt32(yearEl);
            if (je.TryGetProperty("year", out var yearEl2))
                dateValue.Year = ConvertToInt32(yearEl2);
            if (je.TryGetProperty("Month", out var monthEl))
                dateValue.Month = ConvertToInt32(monthEl);
            if (je.TryGetProperty("month", out var monthEl2))
                dateValue.Month = ConvertToInt32(monthEl2);
            if (je.TryGetProperty("Day", out var dayEl))
                dateValue.Day = ConvertToInt32(dayEl);
            if (je.TryGetProperty("day", out var dayEl2))
                dateValue.Day = ConvertToInt32(dayEl2);
            return dateValue;
        }
        
        // Try parsing as DateTime string
        if (obj is string str && DateTime.TryParse(str, out var dt))
        {
            return new DateValue { Year = dt.Year, Month = dt.Month, Day = dt.Day };
        }
        
        return null;
    }

    private static TimeValue? ConvertToTimeValue(object? obj)
    {
        if (obj == null) return null;
        
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var timeValue = new TimeValue();
            if (je.TryGetProperty("Hour", out var hourEl))
                timeValue.Hour = ConvertToInt32(hourEl);
            if (je.TryGetProperty("hour", out var hourEl2))
                timeValue.Hour = ConvertToInt32(hourEl2);
            if (je.TryGetProperty("Minute", out var minuteEl))
                timeValue.Minute = ConvertToInt32(minuteEl);
            if (je.TryGetProperty("minute", out var minuteEl2))
                timeValue.Minute = ConvertToInt32(minuteEl2);
            if (je.TryGetProperty("Second", out var secondEl))
                timeValue.Second = ConvertToInt32(secondEl);
            if (je.TryGetProperty("second", out var secondEl2))
                timeValue.Second = ConvertToInt32(secondEl2);
            return timeValue;
        }
        
        // Try parsing as TimeSpan string
        if (obj is string str && TimeSpan.TryParse(str, out var ts))
        {
            return new TimeValue { Hour = ts.Hours, Minute = ts.Minutes, Second = ts.Seconds };
        }
        
        return null;
    }

    private static TEnum ConvertToEnum<TEnum>(object? obj, TEnum defaultValue) where TEnum : struct
    {
        if (obj == null) return defaultValue;
        if (obj is TEnum directEnum) return directEnum;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var enumInt))
        {
            if (System.Enum.IsDefined(typeof(TEnum), enumInt))
                return (TEnum)System.Enum.ToObject(typeof(TEnum), enumInt);
        }
        if (System.Enum.TryParse(obj.ToString(), true, out TEnum parsed))
            return parsed;
        return defaultValue;
    }

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private static bool ConvertToBool(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (obj is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        if (bool.TryParse(obj.ToString(), out var result)) return result;
        return false;
    }

    private static int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }

    private static DateTime? ConvertToDateTime(object? obj)
    {
        if (obj == null) return null;
        if (obj is DateTime dt) return dt;
        if (obj is DateTimeOffset dto) return dto.DateTime;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(je.GetString(), out var dt2)) return dt2;
        }
        if (DateTime.TryParse(obj.ToString(), out var dt3)) return dt3;
        return null;
    }
}
