using System;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Controllers;

/// <summary>
/// Helper class for formatting DateTime and Timestamp to ISO 8601 string format
/// </summary>
public static class DateTimeFormatHelper
{
    /// <summary>
    /// Convert DateTime to ISO 8601 string format (e.g., "2026-01-15T10:30:00.000Z")
    /// </summary>
    public static string ToIso8601String(DateTime dateTime)
    {
        return dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    /// <summary>
    /// Convert nullable DateTime to ISO 8601 string format
    /// </summary>
    public static string? ToIso8601String(DateTime? dateTime)
    {
        return dateTime.HasValue ? ToIso8601String(dateTime.Value) : null;
    }

    /// <summary>
    /// Convert Protobuf Timestamp to ISO 8601 string format
    /// Handles both nullable and non-nullable Timestamp
    /// </summary>
    public static string ToIso8601String(Timestamp timestamp)
    {
        if (timestamp == null)
        {
            return string.Empty;
        }
        var utcDateTime = timestamp.ToDateTime();
        return ToIso8601String(utcDateTime);
    }
}
