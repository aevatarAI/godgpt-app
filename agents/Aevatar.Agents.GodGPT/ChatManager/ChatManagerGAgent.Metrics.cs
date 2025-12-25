using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Observability;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    /// <summary>
    /// Records user activity metrics for retention analysis
    /// Application-level deduplication: Check SessionInfoList to determine if today's activity was already reported
    /// </summary>
    private async Task RecordUserActivityMetricsAsync()
    {
        try
        {
            var today = DateTime.UtcNow.Date;

            // Check SessionInfoList for the last session's creation time
            var lastSession = State.SessionInfoList?.LastOrDefault();
            if (lastSession != null && lastSession.CreateAt.Date() == today)
            {
                // Today already has session creation, skip duplicate reporting
                Logger.LogDebug(
                    "[GodChatGAgent][RecordUserActivityMetricsAsync] {UserId} User activity metrics already recorded today",
                    Id.ToString());
                return;
            }

            // First session creation today, need to record metrics
            var todayString = today.ToString("yyyy-MM-dd");
            var userRegistrationDate = State.RegisteredAtUtc?.ToFormattedString("yyyy-MM-dd") ?? todayString;

            // Get user membership level
            var membershipLevel = await DetermineMembershipLevelAsync();

            // Calculate days since registration
            var registrationDate = State.RegisteredAtUtc?.Date() ?? today;
            var daysSinceRegistration = (int)(today - registrationDate).TotalDays;

            // Record user activity metrics (ensure each user is counted only once per day)
            UserLifecycleTelemetryMetrics.RecordUserActivityByCohort(
                daysSinceRegistration: daysSinceRegistration,
                membershipLevel: membershipLevel,
                logger: Logger);

            Logger.LogInformation(
                "User activity metrics recorded: daysSinceRegistration={DaysSinceRegistration}, membership={MembershipLevel}",
                daysSinceRegistration, membershipLevel);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to record user activity metrics for retention analysis");
        }
    }

    /// <summary>
    /// Determines user membership level based on UserQuotaGAgent subscription information
    /// Returns one of 9 levels defined in UserMembershipTier constants:
    /// Free, PremiumDay, PremiumWeek, PremiumMonth, PremiumYear,
    /// UltimateDay, UltimateWeek, UltimateMonth, UltimateYear
    /// </summary>
    private async Task<string> DetermineMembershipLevelAsync()
    {
        try
        {
            var userQuotaGrain = await GetUserQuotaAgentAsync(Id);

            // Check Ultimate subscription first (higher priority)
            var ultimateSubscription = await userQuotaGrain.GetSubscriptionAsync(ultimate: true);
            if (ultimateSubscription.IsActive)
            {
                return ultimateSubscription.PlanType switch
                {
                    PlanType.Day => UserMembershipTier.UltimateDay,
                    PlanType.Week => UserMembershipTier.UltimateWeek,
                    PlanType.Month => UserMembershipTier.UltimateMonth,
                    PlanType.Year => UserMembershipTier.UltimateYear,
                    _ => UserMembershipTier.UltimateMonth // Default fallback for unknown plan types
                };
            }

            // Check Premium subscription
            var premiumSubscription = await userQuotaGrain.GetSubscriptionAsync(ultimate: false);
            if (premiumSubscription.IsActive)
            {
                return premiumSubscription.PlanType switch
                {
                    PlanType.Day => UserMembershipTier.PremiumDay,
                    PlanType.Week => UserMembershipTier.PremiumWeek,
                    PlanType.Month => UserMembershipTier.PremiumMonth,
                    PlanType.Year => UserMembershipTier.PremiumYear,
                    _ => UserMembershipTier.PremiumMonth // Default fallback for unknown plan types
                };
            }

            // No active subscription
            return UserMembershipTier.Free;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to determine membership level from UserQuotaGAgent, defaulting to 'free'");
            return UserMembershipTier.Free;
        }
    }
}

