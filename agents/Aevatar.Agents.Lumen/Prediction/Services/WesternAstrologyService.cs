using Microsoft.Extensions.Logging;
using SwissEphNet;

namespace Aevatar.Agents.Lumen.Prediction.Services;

/// <summary>
/// Western Astrology calculation service using Swiss Ephemeris
/// Calculates Sun Sign, Moon Sign, and Rising Sign (Ascendant)
/// </summary>
public static class WesternAstrologyService
{
    // Zodiac sign names in order (0-11)
    private static readonly string[] ZodiacSigns =
    {
        "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
        "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
    };

    /// <summary>
    /// Calculate all three signs: Sun, Moon, and Rising using Swiss Ephemeris
    /// </summary>
    /// <param name="birthDate">Birth date</param>
    /// <param name="birthTime">Birth time (required for Moon and Rising)</param>
    /// <param name="latitude">Latitude for Rising sign calculation</param>
    /// <param name="longitude">Longitude for Rising sign calculation</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>Tuple of (sunSign, moonSign, risingSign)</returns>
    public static (string sunSign, string moonSign, string risingSign) CalculateSigns(
        DateOnly birthDate,
        TimeOnly birthTime,
        double latitude,
        double longitude,
        ILogger? logger = null)
    {
        try
        {
            logger?.LogInformation(
                "[WesternAstrologyService] Calculating signs for {Date} {Time} at ({Lat}, {Lon})",
                birthDate, birthTime, latitude, longitude);

            // Create SwissEph instance for this calculation
            using var swissEph = new SwissEph();

            // Convert to Julian Day (UTC)
            var birthDateTime = birthDate.ToDateTime(birthTime);
            double julianDay = ToJulianDay(swissEph, birthDateTime, logger);

            // Calculate all three signs
            string sunSign = CalculateSunSign(swissEph, julianDay, logger);
            string moonSign = CalculateMoonSign(swissEph, julianDay, logger);
            string risingSign = CalculateRisingSign(swissEph, julianDay, latitude, longitude, logger);

            logger?.LogInformation(
                "[WesternAstrologyService] Results - Sun: {Sun}, Moon: {Moon}, Rising: {Rising}",
                sunSign, moonSign, risingSign);

            return (sunSign, moonSign, risingSign);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "[WesternAstrologyService] Failed to calculate signs for ({Lat}, {Lon})",
                latitude, longitude);

            // Fallback: use simple Sun sign calculation
            string sunSign = CalculateSunSignSimple(birthDate);
            return (sunSign, sunSign, sunSign);
        }
    }

    /// <summary>
    /// Calculate Sun Sign using Swiss Ephemeris
    /// </summary>
    private static string CalculateSunSign(SwissEph swissEph, double julianDay, ILogger? logger)
    {
        try
        {
            double[] positions = new double[6];
            string? errorMsg = null;

            int result = swissEph.swe_calc_ut(
                julianDay,
                SwissEph.SE_SUN,
                SwissEph.SEFLG_SWIEPH,
                positions,
                ref errorMsg);

            if (result < 0)
            {
                logger?.LogWarning(
                    "[WesternAstrologyService] Swiss Ephemeris Sun calculation failed: {Error}",
                    errorMsg);
                return "Aries"; // Fallback
            }

            // positions[0] is longitude in degrees (0-360)
            // Each zodiac sign is 30 degrees
            int signIndex = (int)(positions[0] / 30.0);
            logger?.LogDebug(
                "[WesternAstrologyService] Sun position: {Position}° -> {Sign}",
                positions[0], ZodiacSigns[signIndex % 12]);

            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WesternAstrologyService] Sun sign calculation error");
            return "Aries";
        }
    }

    /// <summary>
    /// Calculate Moon Sign using Swiss Ephemeris
    /// </summary>
    private static string CalculateMoonSign(SwissEph swissEph, double julianDay, ILogger? logger)
    {
        try
        {
            double[] positions = new double[6];
            string? errorMsg = null;

            int result = swissEph.swe_calc_ut(
                julianDay,
                SwissEph.SE_MOON,
                SwissEph.SEFLG_SWIEPH,
                positions,
                ref errorMsg);

            if (result < 0)
            {
                logger?.LogWarning(
                    "[WesternAstrologyService] Swiss Ephemeris Moon calculation failed: {Error}",
                    errorMsg);
                return "Aries"; // Fallback
            }

            int signIndex = (int)(positions[0] / 30.0);
            logger?.LogDebug(
                "[WesternAstrologyService] Moon position: {Position}° -> {Sign}",
                positions[0], ZodiacSigns[signIndex % 12]);

            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WesternAstrologyService] Moon sign calculation error");
            return "Aries";
        }
    }

    /// <summary>
    /// Calculate Rising Sign (Ascendant) using Swiss Ephemeris
    /// </summary>
    private static string CalculateRisingSign(
        SwissEph swissEph,
        double julianDay,
        double latitude,
        double longitude,
        ILogger? logger)
    {
        try
        {
            double[] cusps = new double[13];
            double[] ascmc = new double[10];

            int result = swissEph.swe_houses(
                julianDay,
                latitude,
                longitude,
                'P', // Placidus house system (most common)
                cusps,
                ascmc);

            if (result < 0)
            {
                logger?.LogWarning(
                    "[WesternAstrologyService] Swiss Ephemeris Ascendant calculation failed");
                return "Aries"; // Fallback
            }

            // ascmc[0] is the Ascendant (Rising Sign) longitude
            int signIndex = (int)(ascmc[0] / 30.0);
            logger?.LogDebug(
                "[WesternAstrologyService] Rising position: {Position}° -> {Sign}",
                ascmc[0], ZodiacSigns[signIndex % 12]);

            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WesternAstrologyService] Rising sign calculation error");
            return "Aries";
        }
    }

    /// <summary>
    /// Convert DateTime to Julian Day Number (for Swiss Ephemeris)
    /// Input dateTime should be local birth time (NOT UTC converted)
    /// </summary>
    private static double ToJulianDay(SwissEph swissEph, DateTime dateTime, ILogger? logger)
    {
        // Specify as UTC kind for calculation purposes
        // Note: Birth time should be LOCAL time for astrology calculations
        dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

        int year = dateTime.Year;
        int month = dateTime.Month;
        int day = dateTime.Day;
        double hour = dateTime.Hour + dateTime.Minute / 60.0 + dateTime.Second / 3600.0;

        double julianDay = swissEph.swe_julday(year, month, day, hour, SwissEph.SE_GREG_CAL);
        logger?.LogDebug(
            "[WesternAstrologyService] Julian Day: {JD} for {DateTime:yyyy-MM-dd HH:mm:ss}",
            julianDay, dateTime);

        return julianDay;
    }

    /// <summary>
    /// Simple Sun sign calculation based on date only (fallback method)
    /// </summary>
    public static string CalculateSunSignSimple(DateOnly birthDate)
    {
        int month = birthDate.Month;
        int day = birthDate.Day;

        return (month, day) switch
        {
            (3, >= 21) or (4, <= 19) => "Aries",
            (4, >= 20) or (5, <= 20) => "Taurus",
            (5, >= 21) or (6, <= 20) => "Gemini",
            (6, >= 21) or (7, <= 22) => "Cancer",
            (7, >= 23) or (8, <= 22) => "Leo",
            (8, >= 23) or (9, <= 22) => "Virgo",
            (9, >= 23) or (10, <= 22) => "Libra",
            (10, >= 23) or (11, <= 21) => "Scorpio",
            (11, >= 22) or (12, <= 21) => "Sagittarius",
            (12, >= 22) or (1, <= 19) => "Capricorn",
            (1, >= 20) or (2, <= 18) => "Aquarius",
            _ => "Pisces" // Feb 19 - Mar 20
        };
    }

    /// <summary>
    /// Parse latitude and longitude from LatLong string
    /// </summary>
    /// <param name="latLong">Format: "latitude,longitude" e.g., "34.0522,-118.2437"</param>
    /// <returns>Tuple of (latitude, longitude) or null if parsing fails</returns>
    public static (double latitude, double longitude)? ParseLatLong(string? latLong)
    {
        if (string.IsNullOrWhiteSpace(latLong))
            return null;

        var parts = latLong.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return null;

        if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double latitude) &&
            double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double longitude))
        {
            return (latitude, longitude);
        }

        return null;
    }
}

