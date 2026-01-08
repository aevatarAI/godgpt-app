namespace Aevatar.Agents.Lumen.Helpers;

/// <summary>
/// Timezone helper for Lumen module
/// Converts local time at specific coordinates to UTC
/// 
/// TODO: Install GeoTimeZone and TimeZoneConverter packages for full functionality
/// Current implementation uses a simplified longitude-based approximation
/// </summary>
public static class LumenTimezoneHelper
{
    /// <summary>
    /// Converts a local "wall clock" time at a specific location to UTC time
    /// </summary>
    /// <param name="localDateTime">The local date and time (Kind should be Unspecified)</param>
    /// <param name="latitude">Location latitude</param>
    /// <param name="longitude">Location longitude</param>
    /// <returns>Tuple containing UTC DateTime, the Offset used, and the Timezone ID</returns>
    public static (DateTime UtcTime, TimeSpan Offset, string TimezoneId) GetUtcTimeFromLocal(
        DateTime localDateTime, 
        double latitude, 
        double longitude)
    {
        // Simplified implementation using longitude-based approximation
        // For production use, install GeoTimeZone and TimeZoneConverter packages
        
        // Longitude-based approximation: 15 degrees = 1 hour
        // This is an approximation and may not be accurate near timezone boundaries
        var approxOffsetHours = Math.Round(longitude / 15.0);
        var approxOffset = TimeSpan.FromHours(approxOffsetHours);
        var utcTime = localDateTime.Add(-approxOffset);
        
        return (utcTime, approxOffset, "UTC_Approx");
    }

    /// <summary>
    /// Get timezone offset for a location at a specific date
    /// </summary>
    /// <param name="latitude">Location latitude</param>
    /// <param name="longitude">Location longitude</param>
    /// <param name="dateTime">Date/time for DST calculation</param>
    /// <returns>TimeSpan offset from UTC</returns>
    public static TimeSpan GetTimezoneOffset(double latitude, double longitude, DateTime dateTime)
    {
        // Simplified: longitude / 15 hours offset
        var hours = Math.Round(longitude / 15.0);
        return TimeSpan.FromHours(hours);
    }

    /// <summary>
    /// Get timezone ID for a location
    /// </summary>
    /// <param name="latitude">Location latitude</param>
    /// <param name="longitude">Location longitude</param>
    /// <returns>Timezone ID (IANA format when full implementation available)</returns>
    public static string GetTimezoneId(double latitude, double longitude)
    {
        // TODO: Use GeoTimeZone.TimeZoneLookup.GetTimeZone for accurate lookup
        return "UTC_Approx";
    }
}

