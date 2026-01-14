using System;
using System.Collections.Generic;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// Converter for AIAgentStatusProxy State from old JSON format to new Protobuf format
/// </summary>
public class AIAgentStatusProxyStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        try
        {
            var newState = new AIAgentStatusProxyStateProto();

            // is_available (bool)
            newState.IsAvailable = ConvertToBool(oldState.GetValueOrDefault("IsAvailable")) ?? true;

            // unavailable_since (optional Timestamp)
            if (oldState.TryGetValue("UnavailableSince", out var unavailableSinceObj) && unavailableSinceObj != null)
            {
                var unavailableSince = ConvertToDateTime(unavailableSinceObj);
                if (unavailableSince.HasValue)
                {
                    newState.UnavailableSince = Timestamp.FromDateTime(unavailableSince.Value.ToUniversalTime());
                }
            }

            // recovery_delay (Duration) - from RecoveryDelay TimeSpan
            if (oldState.TryGetValue("RecoveryDelay", out var recoveryDelayObj))
            {
                var duration = ConvertToTimeSpan(recoveryDelayObj);
                if (duration.HasValue)
                {
                    newState.RecoveryDelay = Duration.FromTimeSpan(duration.Value);
                }
                else
                {
                    // Default to 1 hour if not found
                    newState.RecoveryDelay = Duration.FromTimeSpan(TimeSpan.FromHours(1));
                }
            }
            else
            {
                newState.RecoveryDelay = Duration.FromTimeSpan(TimeSpan.FromHours(1));
            }

            // unavailable_count (int64)
            newState.UnavailableCount = ConvertToInt64(oldState.GetValueOrDefault("UnavailableCount")) ?? 0;

            // exception_count (int64)
            newState.ExceptionCount = ConvertToInt64(oldState.GetValueOrDefault("ExceptionCount")) ?? 0;

            // parent_id (string) - Guid as string
            newState.ParentId = ConvertToString(oldState.GetValueOrDefault("ParentId")) ?? string.Empty;

            // prompt_template (string)
            newState.PromptTemplate = ConvertToString(oldState.GetValueOrDefault("PromptTemplate")) ?? string.Empty;

            return newState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AIAgentStatusProxyStateConverter] Conversion failed: {ex.Message}");
            return null;
        }
    }

    private static bool? ConvertToBool(object? value)
    {
        if (value == null) return null;
        if (value is bool b) return b;
        if (bool.TryParse(value.ToString(), out var result)) return result;
        return null;
    }

    private static string? ConvertToString(object? value)
    {
        if (value == null) return null;
        return value.ToString();
    }

    private static long? ConvertToInt64(object? value)
    {
        if (value == null) return null;
        if (value is long l) return l;
        if (value is int i) return i;
        if (long.TryParse(value.ToString(), out var result)) return result;
        return null;
    }

    private static DateTime? ConvertToDateTime(object? value)
    {
        if (value == null) return null;
        if (value is DateTime dt) return dt;
        if (value is string str && DateTime.TryParse(str, out var dt2)) return dt2;
        return null;
    }

    private static TimeSpan? ConvertToTimeSpan(object? value)
    {
        if (value == null) return null;
        if (value is TimeSpan ts) return ts;
        if (value is Dictionary<string, object?> dict)
        {
            // Handle TimeSpan-like dictionary structure from JSON
            if (dict.TryGetValue("TotalHours", out var totalHoursObj) && double.TryParse(totalHoursObj?.ToString(), out var totalHours))
            {
                return TimeSpan.FromHours(totalHours);
            }
            if (dict.TryGetValue("Hours", out var hoursObj) && int.TryParse(hoursObj?.ToString(), out var hours))
            {
                return TimeSpan.FromHours(hours);
            }
            if (dict.TryGetValue("Ticks", out var ticksObj) && long.TryParse(ticksObj?.ToString(), out var ticks))
            {
                return TimeSpan.FromTicks(ticks);
            }
        }
        if (value is string str && TimeSpan.TryParse(str, out var ts2)) return ts2;
        return null;
    }
}
