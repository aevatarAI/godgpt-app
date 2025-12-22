using Aevatar.Application.Grains.Common.Constants;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Application.Grains.Common.Helpers;

/// <summary>
/// Extension methods for converting between Protobuf types and C# types
/// NOTE: PlanType and PaymentStatus conversions removed - now using Proto types from user_quota.proto everywhere
/// </summary>
public static class ProtoConversions
{
    #region Timestamp Conversions
    
    /// <summary>
    /// Convert Timestamp to DateTime
    /// </summary>
    public static DateTime ToDateTime(this Timestamp? timestamp)
    {
        if (timestamp == null)
            return DateTime.MinValue;
        return timestamp.ToDateTime();
    }
    
    /// <summary>
    /// Convert DateTime to Timestamp
    /// </summary>
    public static Timestamp ToProtoTimestamp(this DateTime dateTime)
    {
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
    }
    
    public static Timestamp ToTimestamp(this DateTime dateTime)
    {
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
    }
    
    /// <summary>
    /// Convert DateTime? to Timestamp
    /// </summary>
    public static Timestamp? ToProtoTimestampNullable(this DateTime? dateTime)
    {
        if (!dateTime.HasValue)
            return null;
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc));
    }

    /// <summary>
    /// Add days to a Timestamp and return a new Timestamp
    /// </summary>
    public static Timestamp AddDays(this Timestamp timestamp, double days)
    {
        var secondsToAdd = (long)(days * 86400); // 86400 seconds per day
        return timestamp + Duration.FromTimeSpan(TimeSpan.FromSeconds(secondsToAdd));
    }
    
    #endregion
    
    #region PaymentPlatform Conversions
    
    /// <summary>
    /// Convert int to PaymentPlatform
    /// </summary>
    public static PaymentPlatform ToPaymentPlatform(this int value)
    {
        return (PaymentPlatform)value;
    }
    
    /// <summary>
    /// Convert PaymentPlatform to int
    /// </summary>
    public static int ToInt(this PaymentPlatform platform)
    {
        return (int)platform;
    }
    
    #endregion
}

