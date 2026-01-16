using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// LumenPrediction State converter
/// </summary>
public class LumenPredictionStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new LumenPredictionState();

        var newState = new LumenPredictionState();

        if (oldState.TryGetValue("PredictionId", out var predictionIdObj))
            newState.PredictionId = ConvertToString(predictionIdObj);

        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        if (oldState.TryGetValue("PredictionDate", out var predictionDateObj))
        {
            var dateValue = ConvertToDateValue(predictionDateObj);
            if (dateValue != null)
                newState.PredictionDate = dateValue;
        }

        if (oldState.TryGetValue("Type", out var typeObj))
            newState.Type = ConvertToEnum<PredictionType>(typeObj, PredictionType.PredictionDaily);

        // Results map
        if (oldState.TryGetValue("Results", out var resultsObj))
        {
            if (resultsObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in je.EnumerateObject())
                {
                    newState.Results[prop.Name] = ConvertToString(prop.Value);
                }
            }
            else if (resultsObj is Dictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    newState.Results[kvp.Key] = ConvertToString(kvp.Value);
                }
            }
        }

        // Timestamps
        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("ProfileUpdatedAt", out var profileUpdatedAtObj))
        {
            var dt = ConvertToDateTime(profileUpdatedAtObj);
            if (dt.HasValue)
                newState.ProfileUpdatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("LastActiveDate", out var lastActiveDateObj))
        {
            var dt = ConvertToDateTime(lastActiveDateObj);
            if (dt.HasValue)
                newState.LastActiveDate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("IsDailyReminderEnabled", out var isDailyReminderEnabledObj))
            newState.IsDailyReminderEnabled = ConvertToBool(isDailyReminderEnabledObj);

        if (oldState.TryGetValue("DailyReminderTargetId", out var dailyReminderTargetIdObj))
            newState.DailyReminderTargetId = ConvertToString(dailyReminderTargetIdObj);

        return newState;
    }

    private static DateValue? ConvertToDateValue(object? obj)
    {
        if (obj == null) return null;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var dateValue = new DateValue();
            if (je.TryGetProperty("Year", out var yearEl) || je.TryGetProperty("year", out yearEl))
                dateValue.Year = ConvertToInt32(yearEl);
            if (je.TryGetProperty("Month", out var monthEl) || je.TryGetProperty("month", out monthEl))
                dateValue.Month = ConvertToInt32(monthEl);
            if (je.TryGetProperty("Day", out var dayEl) || je.TryGetProperty("day", out dayEl))
                dateValue.Day = ConvertToInt32(dayEl);
            return dateValue;
        }
        if (obj is string str && DateTime.TryParse(str, out var dt))
            return new DateValue { Year = dt.Year, Month = dt.Month, Day = dt.Day };
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
