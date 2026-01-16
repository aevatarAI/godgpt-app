using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// LumenDailyYearlyHistory State converter
/// </summary>
public class LumenDailyYearlyHistoryStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new LumenDailyYearlyHistoryState();

        var newState = new LumenDailyYearlyHistoryState();

        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        if (oldState.TryGetValue("Year", out var yearObj))
            newState.Year = ConvertToInt32(yearObj);

        // Predictions map
        if (oldState.TryGetValue("Predictions", out var predictionsObj))
        {
            if (predictionsObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in je.EnumerateObject())
                {
                    var record = ConvertToDailyPredictionRecord(prop.Value);
                    if (record != null)
                        newState.Predictions[prop.Name] = record;
                }
            }
        }

        if (oldState.TryGetValue("LastUpdatedAt", out var lastUpdatedAtObj))
        {
            var dt = ConvertToDateTime(lastUpdatedAtObj);
            if (dt.HasValue)
                newState.LastUpdatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        return newState;
    }

    private static DailyPredictionRecord? ConvertToDailyPredictionRecord(object? obj)
    {
        if (obj == null) return null;
        
        var record = new DailyPredictionRecord();
        
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("PredictionId", out var predictionIdEl) || je.TryGetProperty("predictionId", out predictionIdEl))
                record.PredictionId = ConvertToString(predictionIdEl);
            
            if (je.TryGetProperty("Date", out var dateEl) || je.TryGetProperty("date", out dateEl))
            {
                var dateValue = ConvertToDateValue(dateEl);
                if (dateValue != null)
                    record.Date = dateValue;
            }
            
            if (je.TryGetProperty("CreatedAt", out var createdAtEl) || je.TryGetProperty("createdAt", out createdAtEl))
            {
                var dt = ConvertToDateTime(createdAtEl);
                if (dt.HasValue)
                    record.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
        }
        
        return record;
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

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
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
