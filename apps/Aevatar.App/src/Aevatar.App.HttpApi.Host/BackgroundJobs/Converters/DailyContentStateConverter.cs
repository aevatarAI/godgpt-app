using System;
using System.Collections.Generic;
using Aevatar.Agents.GodGPT.Protos.DailyPush;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// Converter for DailyContentGAgent State from old JSON format to new Protobuf format
/// </summary>
public class DailyContentStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        try
        {
            var newState = new DailyContentState();

            // contents (map<string, DailyNotificationContentProto>)
            // If old state has ContentId, Title, Content, create a single content entry
            if (oldState.TryGetValue("ContentId", out var contentIdObj))
            {
                var contentId = ConvertToString(contentIdObj) ?? "default-content";
                var content = new DailyNotificationContentProto
                {
                    Id = contentId,
                    IsActive = ConvertToBool(oldState.GetValueOrDefault("Status")) == true || 
                               ConvertToString(oldState.GetValueOrDefault("Status"))?.ToLower() == "scheduled"
                };

                // localized_contents (map<string, LocalizedContentDataProto>)
                var title = ConvertToString(oldState.GetValueOrDefault("Title")) ?? string.Empty;
                var contentText = ConvertToString(oldState.GetValueOrDefault("Content")) ?? string.Empty;
                
                // Default to "en" locale if no language specified
                content.LocalizedContents["en"] = new LocalizedContentDataProto
                {
                    Title = title,
                    Content = contentText
                };

                newState.Contents[contentId] = content;
            }

            // daily_usage_history (map<string, string>)
            // If old state has ScheduledTime, add to usage history
            if (oldState.TryGetValue("ScheduledTime", out var scheduledTimeObj) && scheduledTimeObj != null)
            {
                var scheduledTime = ConvertToDateTime(scheduledTimeObj);
                if (scheduledTime.HasValue)
                {
                    var dateKey = scheduledTime.Value.ToString("yyyy-MM-dd");
                    var contentId = ConvertToString(oldState.GetValueOrDefault("ContentId")) ?? "default-content";
                    newState.DailyUsageHistory[dateKey] = contentId;
                }
            }

            // last_refresh (Timestamp)
            if (oldState.TryGetValue("CreatedAt", out var createdAtObj) && createdAtObj != null)
            {
                var createdAt = ConvertToDateTime(createdAtObj);
                if (createdAt.HasValue)
                {
                    newState.LastRefresh = Timestamp.FromDateTime(createdAt.Value.ToUniversalTime());
                }
            }

            // last_selection (Timestamp)
            if (oldState.TryGetValue("ScheduledTime", out var scheduledTimeObj2) && scheduledTimeObj2 != null)
            {
                var scheduledTime = ConvertToDateTime(scheduledTimeObj2);
                if (scheduledTime.HasValue)
                {
                    newState.LastSelection = Timestamp.FromDateTime(scheduledTime.Value.ToUniversalTime());
                }
            }

            // selection_count (int32)
            newState.SelectionCount = ConvertToInt32(oldState.GetValueOrDefault("SelectionCount")) ?? 0;

            // timezone_guid_mappings (map<string, string>) - empty if not found
            // daily_selected_content_cache (map<string, string>) - empty if not found

            return newState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DailyContentStateConverter] Conversion failed: {ex.Message}");
            return null;
        }
    }

    private static string? ConvertToString(object? value)
    {
        if (value == null) return null;
        return value.ToString();
    }

    private static bool? ConvertToBool(object? value)
    {
        if (value == null) return null;
        if (value is bool b) return b;
        if (bool.TryParse(value.ToString(), out var result)) return result;
        return null;
    }

    private static int? ConvertToInt32(object? value)
    {
        if (value == null) return null;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (int.TryParse(value.ToString(), out var result)) return result;
        return null;
    }

    private static DateTime? ConvertToDateTime(object? value)
    {
        if (value == null) return null;
        if (value is DateTime dt) return dt;
        if (value is string str && DateTime.TryParse(str, out var dt2)) return dt2;
        return null;
    }
}
