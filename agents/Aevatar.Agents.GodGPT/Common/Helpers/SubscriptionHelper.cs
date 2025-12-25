using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Application.Grains.Common.Helpers;

/// <summary>
/// Subscription utility methods for Ultimate mode and historical compatibility
/// </summary>
public static class SubscriptionHelper
{
    /// <summary>
    /// Checks if the subscription is Ultimate based on configuration flag
    /// </summary>
    public static bool IsUltimateSubscription(bool isUltimate)
    {
        return isUltimate;
    }

    /// <summary>
    /// Legacy method for backward compatibility - checks if plan type suggests Ultimate
    /// Note: This is deprecated and should be replaced with configuration-driven approach
    /// </summary>
    [Obsolete("Use IsUltimateSubscription(bool isUltimate) instead for configuration-driven Ultimate detection")]
    public static bool IsUltimateSubscription(QuotaPlanType planType)
    {
        // Legacy hardcoded logic - kept for backward compatibility during migration
        return false; // All plans are now standard by default, Ultimate is configuration-driven
    }

    /// <summary>
    /// Checks if the plan type is a standard subscription
    /// </summary>
    public static bool IsStandardSubscription(QuotaPlanType planType)
    {
        return planType is QuotaPlanType.Day 
            or QuotaPlanType.Week 
            or QuotaPlanType.Month 
            or QuotaPlanType.Year;
    }

    /// <summary>
    /// Normalizes plan type for business logic (treats legacy Day as Week)
    /// </summary>
    public static QuotaPlanType NormalizePlanType(QuotaPlanType planType)
    {
        // Treat legacy Day as Week for business logic
        return planType == QuotaPlanType.Day ? QuotaPlanType.Week : planType;
    }

    /// <summary>
    /// Gets display-friendly plan name with Ultimate suffix based on configuration
    /// </summary>
    public static string GetPlanDisplayName(QuotaPlanType planType, bool isUltimate = false)
    {
        var baseName = planType switch
        {
            QuotaPlanType.Day => "Weekly",  // Display legacy Day as Weekly
            QuotaPlanType.Week => "Weekly",
            QuotaPlanType.Month => "Monthly",
            QuotaPlanType.Year => "Annual",
            QuotaPlanType.None => "No Subscription",
            _ => "Unknown"
        };

        return isUltimate ? $"{baseName} Ultimate" : baseName;
    }

    /// <summary>
    /// Legacy method for backward compatibility
    /// </summary>
    [Obsolete("Use GetPlanDisplayName(QuotaPlanType planType, bool isUltimate) instead")]
    public static string GetPlanDisplayName(QuotaPlanType planType)
    {
        return GetPlanDisplayName(planType, false);
    }

    /// <summary>
    /// Calculates subscription end date based on plan type (Timestamp version)
    /// </summary>
    public static Timestamp GetSubscriptionEndDate(QuotaPlanType planType, Timestamp startDate)
    {
        var start = startDate!.ToDateTime();
        var endDateTime = GetSubscriptionEndDate(planType, start);
        return Timestamp.FromDateTime(DateTime.SpecifyKind(endDateTime, DateTimeKind.Utc));
    }

    /// <summary>
    /// Calculates subscription end date based on plan type (DateTime version)
    /// </summary>
    public static DateTime GetSubscriptionEndDate(QuotaPlanType planType, DateTime startDate)
    {
        return planType switch
        {
            QuotaPlanType.Day => startDate.AddDays(1),
            QuotaPlanType.Week => startDate.AddDays(7),
            QuotaPlanType.Month => startDate.AddDays(30),
            QuotaPlanType.Year => startDate.AddDays(390),
            _ => throw new ArgumentException($"Invalid plan type: {planType}")
        };
    }

    /// <summary>
    /// Overload: Calculates subscription end date using C# PlanType
    /// </summary>
    public static DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate)
    {
        return GetSubscriptionEndDate(planType.ToQuotaPlanType(), startDate);
    }

    /// <summary>
    /// Gets the number of days for a plan type (used for refund calculations)
    /// </summary>
    public static int GetDaysForPlanType(QuotaPlanType planType)
    {
        return planType switch
        {
            // Historical compatibility: Day treated as 7 days
            QuotaPlanType.Day => 1,
            QuotaPlanType.Week => 7,
            QuotaPlanType.Month => 30,
            QuotaPlanType.Year => 390,
            _ => throw new ArgumentException($"Invalid plan type: {planType}")
        };
    }

    /// <summary>
    /// Calculates daily average price for a plan
    /// </summary>
    public static decimal CalculateDailyAveragePrice(QuotaPlanType planType, decimal amount)
    {
        var days = GetDaysForPlanType(planType);
        return Math.Round(amount / days, 2, MidpointRounding.ToZero);
    }

    /// <summary>
    /// Validates if an upgrade path is allowed (updated for configuration-driven Ultimate)
    /// </summary>
    public static bool IsUpgradePathValid(QuotaPlanType fromPlan, QuotaPlanType toPlan)
    {
        // upgrades based on logical order: Day/Week -> Month/Year, Month -> Year
        var fromOrder = GetPlanTypeLogicalOrder(fromPlan);
        var toOrder = GetPlanTypeLogicalOrder(toPlan);
            
        // Allow upgrades (higher logical order) or same plan (renewal)
        return toOrder > fromOrder;
    }

    /// <summary>
    /// Gets the logical order value for plan type comparison (Day=1, Week=2, Month=3, Year=4)
    /// This handles historical compatibility where enum values don't match logical order
    /// </summary>
    public static int GetPlanTypeLogicalOrder(QuotaPlanType planType)
    {
        return planType switch
        {
            QuotaPlanType.Day => 1,     // Logical order: 1st level
            QuotaPlanType.Week => 2,    // Logical order: 2nd level  
            QuotaPlanType.Month => 3,   // Logical order: 3rd level
            QuotaPlanType.Year => 4,    // Logical order: 4th level
            QuotaPlanType.None => 0,    // No subscription
            _ => 0
        };
    }

    /// <summary>
    /// Compares two plan types based on logical order rather than enum values
    /// Returns: -1 if plan1 < plan2, 0 if equal, 1 if plan1 > plan2
    /// </summary>
    public static int ComparePlanTypes(QuotaPlanType plan1, QuotaPlanType plan2)
    {
        var order1 = GetPlanTypeLogicalOrder(plan1);
        var order2 = GetPlanTypeLogicalOrder(plan2);
        return order1.CompareTo(order2);
    }

    /// <summary>
    /// Checks if target plan is an upgrade from current plan (logical order comparison)
    /// </summary>
    public static bool IsUpgrade(QuotaPlanType fromPlan, QuotaPlanType toPlan)
    {
        return ComparePlanTypes(toPlan, fromPlan) > 0;
    }

    /// <summary>
    /// Checks if target plan is same level or upgrade from current plan (logical order comparison)
    /// </summary>
    public static bool IsUpgradeOrSameLevel(QuotaPlanType fromPlan, QuotaPlanType toPlan)
    {
        return ComparePlanTypes(toPlan, fromPlan) >= 0;
    }
    
    /// <summary>
    /// Overload: Checks if target plan is same level or upgrade (C# PlanType version)
    /// </summary>
    public static bool IsUpgradeOrSameLevel(PlanType fromPlan, PlanType toPlan)
    {
        return IsUpgradeOrSameLevel(fromPlan.ToQuotaPlanType(), toPlan.ToQuotaPlanType());
    }

    public static string GetMembershipLevel(bool isUltimate)
    {
        return isUltimate ? MembershipLevel.Membership_Level_Ultimate : MembershipLevel.Membership_Level_Premium;
    }
} 