using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Application.Grains.Common.Helpers;

/// <summary>
/// Extension methods for converting between Protobuf types and C# types
/// </summary>
public static class ProtoConversions
{
    #region PlanType Conversions
    
    /// <summary>
    /// Convert C# PlanType to Proto QuotaPlanType
    /// </summary>
    public static QuotaPlanType ToQuotaPlanType(this PlanType planType)
    {
        return (QuotaPlanType)(int)planType;
    }
    
    /// <summary>
    /// Convert Proto QuotaPlanType to C# PlanType
    /// </summary>
    public static PlanType ToPlanType(this QuotaPlanType quotaPlanType)
    {
        return (PlanType)(int)quotaPlanType;
    }
    
    #endregion
    
    #region PaymentStatus Conversions
    
    /// <summary>
    /// Convert C# PaymentStatus to Proto QuotaPaymentStatus
    /// </summary>
    public static QuotaPaymentStatus ToQuotaPaymentStatus(this PaymentStatus status)
    {
        return (QuotaPaymentStatus)(int)status;
    }
    
    /// <summary>
    /// Convert Proto QuotaPaymentStatus to C# PaymentStatus
    /// </summary>
    public static PaymentStatus ToPaymentStatus(this QuotaPaymentStatus status)
    {
        return (PaymentStatus)(int)status;
    }
    
    #endregion

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

