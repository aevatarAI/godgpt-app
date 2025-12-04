using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.DailyPush;
using GodGPT.GAgents.DailyPush.Services;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Daily content selection and management GAgent implementation
/// </summary>
[GAgent(nameof(DailyContentGAgent))]
public class DailyContentGAgent : GAgentBase<DailyContentState>, IDailyContentGAgent
{
    private readonly DailyPushContentService? _contentService;
    private readonly Random _random;

    public DailyContentGAgent(
        Guid id,
        DailyPushContentService? contentService = null) : base(id)
    {
        _contentService = contentService;
        _random = new Random();
    }
    
    // State helper methods (moved from State class)
    private HashSet<string> GetUsedContentIds(DateTime date)
    {
        var dateKey = date.ToString("yyyy-MM-dd");
        if (State.DailyUsageHistory.TryGetValue(dateKey, out var usedIds) && !string.IsNullOrEmpty(usedIds))
        {
            return usedIds.Split(',').ToHashSet();
        }
        return new HashSet<string>();
    }
    
    private void MarkContentAsUsed(DateTime date, string contentId)
    {
        var dateKey = date.ToString("yyyy-MM-dd");
        if (State.DailyUsageHistory.TryGetValue(dateKey, out var usedIds))
        {
            var idSet = string.IsNullOrEmpty(usedIds) ? new HashSet<string>() : usedIds.Split(',').ToHashSet();
            idSet.Add(contentId);
            State.DailyUsageHistory[dateKey] = string.Join(",", idSet);
        }
        else
        {
            State.DailyUsageHistory[dateKey] = contentId;
        }
        
        // Clean old history (keep only last 7 days)
        CleanOldHistory(date);
    }
    
    private void CleanOldHistory(DateTime currentDate)
    {
        var cutoffDate = currentDate.AddDays(-DailyPushConstants.CONTENT_HISTORY_DAYS);
        var keysToRemove = new List<string>();

        foreach (var key in State.DailyUsageHistory.Keys)
        {
            if (DateTime.TryParse(key, out var date) && date < cutoffDate)
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            State.DailyUsageHistory.Remove(key);
        }
        
        // Also clean old selection cache (keep only last 7 days)
        var cacheKeysToRemove = new List<string>();
        foreach (var key in State.DailySelectedContentCache.Keys)
        {
            if (DateTime.TryParse(key, out var date) && date < cutoffDate)
            {
                cacheKeysToRemove.Add(key);
            }
        }

        foreach (var key in cacheKeysToRemove)
        {
            State.DailySelectedContentCache.Remove(key);
        }
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Daily content selection and management");
    }

    protected override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        await base.OnActivateAsync(cancellationToken);
        
        Logger.LogInformation("DailyContentGAgent activated");

        // ✅ Pre-register common timezone mappings to prevent orphaned grains
        await EnsureCommonTimezoneMappingsAsync();

        // Auto-refresh content if empty or stale (older than 24 hours)
        var lastRefresh = State.LastRefresh?.ToDateTime() ?? DateTime.MinValue;
        var needsRefresh = State.Contents.Count == 0 ||
                           (DateTime.UtcNow - lastRefresh).TotalHours > 24;

        if (needsRefresh)
        {
            Logger.LogInformation("Content is empty or stale, triggering auto-refresh from local CSV...");
            try
            {
                await RefreshContentsFromSourceAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "⚠️ Auto-refresh failed during activation, will continue with existing content");
            }
        }
        else
        {
            Logger.LogInformation("📚 Content cache is valid: {Count} entries, last refresh: {LastRefresh}",
                State.Contents.Count, lastRefresh);
        }
    }

    protected override void TransitionState(DailyContentState state, IMessage @event)
    {
        switch (@event)
        {
            case AddContentEvent addEvent:
                state.Contents[addEvent.Content.Id] = addEvent.Content;
                state.LastRefresh = addEvent.UpdateTime;
                break;

            case UpdateContentEvent updateEvent:
                if (state.Contents.ContainsKey(updateEvent.ContentId))
                {
                    state.Contents[updateEvent.ContentId] = updateEvent.Content;
                    state.LastRefresh = updateEvent.UpdateTime;
                }
                break;

            case RemoveContentEvent removeEvent:
                state.Contents.Remove(removeEvent.ContentId);
                state.LastRefresh = removeEvent.UpdateTime;
                break;

            case ContentSelectionEvent selectionEvent:
                state.LastSelection = selectionEvent.SelectionDate;
                state.SelectionCount++;
                // Mark contents as used for the date
                var selectionDate = selectionEvent.SelectionDate?.ToDateTime() ?? DateTime.UtcNow;
                foreach (var contentId in selectionEvent.SelectedContentIds)
                {
                    MarkContentAsUsed(selectionDate, contentId);
                }
                break;

            case UpdateDailyContentCacheEvent cacheEvent:
                state.DailySelectedContentCache[cacheEvent.DateKey] = string.Join(",", cacheEvent.SelectedContentIds);
                break;

            case UpdateTimezoneGuidMappingEvent mappingEvent:
                state.TimezoneGuidMappings[mappingEvent.TimezoneGuid] = mappingEvent.TimezoneId;
                break;

            case ImportContentsEvent importEvent:
                foreach (var content in importEvent.Contents)
                {
                    state.Contents[content.Id] = content;
                }
                state.LastRefresh = importEvent.ImportTime;
                break;

            case RefreshContentsEvent refreshEvent:
                state.LastRefresh = refreshEvent.RefreshTime;
                break;

            default:
                Logger.LogDebug($"Unhandled event type: {@event.GetType().Name}");
                break;
        }
    }

    public async Task<List<DailyNotificationContent>> GetSmartSelectedContentsAsync(int count, DateTime targetDate)
    {
        try
        {
            var dateKey = targetDate.ToString("yyyy-MM-dd");
            
            // 🎯 Check if content has already been selected for this date (same-day cache)
            if (State.DailySelectedContentCache.TryGetValue(dateKey, out var cachedContentIdsStr) && !string.IsNullOrEmpty(cachedContentIdsStr))
            {
                var cachedContentIds = cachedContentIdsStr.Split(',').ToList();
                Logger.LogInformation("🔄 Returning cached content selection for {Date}: [{ContentIds}] (ensuring same-day consistency)", 
                    dateKey, cachedContentIdsStr);
                    
                // Return cached content (filter out any inactive contents)
                return cachedContentIds
                    .Select(id => State.Contents.TryGetValue(id, out var content) ? ConvertFromProto(content) : null)
                    .Where(c => c != null && c.IsActive)
                    .Cast<DailyNotificationContent>()
                    .ToList();
            }
            
            var activeContents = State.Contents.Values.Where(c => c.IsActive).Select(ConvertFromProto).ToList();
            if (activeContents.Count == 0)
            {
                Logger.LogWarning("No active contents available for selection on {Date}", targetDate);
                return new List<DailyNotificationContent>();
            }

            Logger.LogDebug("Found {Count} active contents for selection on {Date}",
                activeContents.Count, targetDate);

            // Get used content from history
            var usedContentIds = new HashSet<string>();
            for (int i = 0; i < DailyPushConstants.CONTENT_HISTORY_DAYS; i++)
            {
                var checkDate = targetDate.AddDays(-i);
                var dailyUsed = GetUsedContentIds(checkDate);
                foreach (var id in dailyUsed)
                {
                    usedContentIds.Add(id);
                }
            }

            // Filter out recently used contents
            var availableContents = activeContents.Where(c => !usedContentIds.Contains(c.Id)).ToList();
            if (availableContents.Count < count)
            {
                Logger.LogWarning(
                    "Not enough unused contents ({Available} < {Required}), falling back to all active contents",
                    availableContents.Count, count);
                availableContents = activeContents; // Fallback to all
            }
            else
            {
                Logger.LogDebug("Found {Available} unused contents for selection (required: {Required})",
                    availableContents.Count, count);
            }

            // 🎯 Deterministic selection based on date - ensures global consistency
            // Use target date as seed to guarantee same content selection across all timezones
            var dateSeed = targetDate.ToString("yyyyMMdd").GetHashCode();
            var deterministicRandom = new Random(dateSeed);

            var selectedContents = new List<DailyNotificationContent>();
            var actualCount = Math.Min(count, availableContents.Count);

            Logger.LogInformation(
                "🌍 Global content selection for {Date}: Using deterministic seed {Seed} to ensure timezone consistency",
                targetDate.ToString("yyyy-MM-dd"), dateSeed);

            for (int i = 0; i < actualCount; i++)
            {
                var randomIndex = deterministicRandom.Next(availableContents.Count);
                var selected = availableContents[randomIndex];
                selectedContents.Add(selected);
                availableContents.RemoveAt(randomIndex);

                Logger.LogDebug("📝 Selected content {Index}/{Total}: ID={ContentId}, Title='{Title}'",
                    i + 1, actualCount, selected.Id,
                    selected.LocalizedContents.TryGetValue("en", out var enContent) ? enContent.Title : "N/A");
            }

            // 🎯 Cache the selection result for same-day consistency
            var selectedContentIds = selectedContents.Select(c => c.Id).ToList();
            
            // ✅ Use Event Sourcing for cache update
            var cacheEvent = new UpdateDailyContentCacheEvent { DateKey = dateKey };
            cacheEvent.SelectedContentIds.AddRange(selectedContentIds);
            RaiseEvent(cacheEvent);
            
            // Raise selection event
            var selectionEvent = new ContentSelectionEvent
            {
                SelectionDate = Timestamp.FromDateTime(targetDate.ToUniversalTime()),
                Count = selectedContents.Count
            };
            selectionEvent.SelectedContentIds.AddRange(selectedContentIds);
            RaiseEvent(selectionEvent);

            Logger.LogInformation("✅ Selected and cached {Count} contents for date {Date}: [{ContentIds}]", 
                selectedContents.Count, targetDate.ToString("yyyy-MM-dd"), string.Join(", ", selectedContentIds));
            return selectedContents;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to select contents for date {Date}", targetDate);
            return new List<DailyNotificationContent>();
        }
    }
    
    // Helper methods to convert between C# and Protobuf types
    private DailyNotificationContent ConvertFromProto(DailyNotificationContentProto proto)
    {
        var content = new DailyNotificationContent
        {
            Id = proto.Id,
            IsActive = proto.IsActive,
            LocalizedContents = new Dictionary<string, LocalizedContentData>()
        };
        foreach (var kvp in proto.LocalizedContents)
        {
            content.LocalizedContents[kvp.Key] = new LocalizedContentData
            {
                Title = kvp.Value.Title,
                Content = kvp.Value.Content
            };
        }
        return content;
    }
    
    private DailyNotificationContentProto ConvertToProto(DailyNotificationContent content)
    {
        var proto = new DailyNotificationContentProto
        {
            Id = content.Id,
            IsActive = content.IsActive
        };
        foreach (var kvp in content.LocalizedContents)
        {
            proto.LocalizedContents[kvp.Key] = new LocalizedContentDataProto
            {
                Title = kvp.Value.Title,
                Content = kvp.Value.Content
            };
        }
        return proto;
    }

    public async Task AddContentAsync(DailyNotificationContent content)
    {
        RaiseEvent(new AddContentEvent
        {
            Content = ConvertToProto(content),
            UpdateTime = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        Logger.LogInformation($"Added content: {content.Id}");
    }

    public async Task UpdateContentAsync(string contentId, DailyNotificationContent content)
    {
        if (State.Contents.ContainsKey(contentId))
        {
            RaiseEvent(new UpdateContentEvent
            {
                ContentId = contentId,
                Content = ConvertToProto(content),
                UpdateTime = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            Logger.LogInformation($"Updated content: {contentId}");
        }
    }

    public async Task<List<DailyNotificationContent>> GetAllContentsAsync()
    {
        return State.Contents.Values.Select(ConvertFromProto).ToList();
    }

    public async Task<DailyNotificationContent?> GetContentByIdAsync(string contentId)
    {
        return State.Contents.TryGetValue(contentId, out var content) ? ConvertFromProto(content) : null;
    }

    public async Task<bool> RemoveContentAsync(string contentId)
    {
        if (State.Contents.ContainsKey(contentId))
        {
            RaiseEvent(new RemoveContentEvent
            {
                ContentId = contentId,
                UpdateTime = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            Logger.LogInformation($"Removed content: {contentId}");
            return true;
        }

        return false;
    }

    public async Task<int> GetActiveContentCountAsync()
    {
        return State.Contents.Values.Count(c => c.IsActive);
    }

    public async Task RefreshContentsFromSourceAsync()
    {
        try
        {
            Logger.LogInformation("🔄 Starting content refresh from local CSV file...");

            if (_contentService == null)
            {
                Logger.LogError("❌ DailyPushContentService not available, cannot refresh contents");
                return;
            }

            // Load all available content from CSV
            var csvContents = await _contentService.GetAllContentsAsync();
            if (csvContents.Count == 0)
            {
                Logger.LogWarning("⚠️ No contents loaded from CSV source");
                return;
            }

            Logger.LogInformation("📥 Loaded {Count} contents from CSV, converting to DailyNotificationContent...",
                csvContents.Count);

            // Convert CSV content to DailyNotificationContent objects
            var convertedContents = new List<DailyNotificationContentProto>();

            foreach (var csvContent in csvContents)
            {
                try
                {
                    var notificationContent = new DailyNotificationContentProto
                    {
                        Id = csvContent.ContentKey,
                        IsActive = true
                    };

                    // Add English content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleEn) || !string.IsNullOrEmpty(csvContent.ContentEn))
                    {
                        notificationContent.LocalizedContents["en"] = new LocalizedContentDataProto
                        {
                            Title = csvContent.TitleEn ?? "",
                            Content = csvContent.ContentEn ?? ""
                        };
                    }

                    // Add Traditional Chinese content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleZh) || !string.IsNullOrEmpty(csvContent.ContentZh))
                    {
                        notificationContent.LocalizedContents["zh-tw"] = new LocalizedContentDataProto
                        {
                            Title = csvContent.TitleZh ?? "",
                            Content = csvContent.ContentZh ?? ""
                        };
                    }

                    // Add Spanish content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleEs) || !string.IsNullOrEmpty(csvContent.ContentEs))
                    {
                        notificationContent.LocalizedContents["es"] = new LocalizedContentDataProto
                        {
                            Title = csvContent.TitleEs ?? "",
                            Content = csvContent.ContentEs ?? ""
                        };
                    }

                    // Add Simplified Chinese content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleZhSc) || !string.IsNullOrEmpty(csvContent.ContentZhSc))
                    {
                        notificationContent.LocalizedContents["zh"] = new LocalizedContentDataProto
                        {
                            Title = csvContent.TitleZhSc ?? "",
                            Content = csvContent.ContentZhSc ?? ""
                        };
                    }

                    convertedContents.Add(notificationContent);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "⚠️ Failed to convert CSV content {ContentKey}", csvContent.ContentKey);
                }
            }

            if (convertedContents.Count == 0)
            {
                Logger.LogError("❌ No valid contents could be converted from CSV");
                return;
            }

            // Import all converted contents
            var importEvent = new ImportContentsEvent { ImportTime = Timestamp.FromDateTime(DateTime.UtcNow) };
            importEvent.Contents.AddRange(convertedContents);
            RaiseEvent(importEvent);

            // Mark refresh completed
            RaiseEvent(new RefreshContentsEvent
            {
                RefreshTime = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            Logger.LogInformation("✅ Content refresh completed: {Count} contents imported from local CSV file",
                convertedContents.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "💥 Critical error during content refresh from local CSV file");

            // Still mark refresh time even if failed
            RaiseEvent(new RefreshContentsEvent
            {
                RefreshTime = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
    }

    public async Task ImportContentsAsync(List<DailyNotificationContent> contents)
    {
        var importEvent = new ImportContentsEvent { ImportTime = Timestamp.FromDateTime(DateTime.UtcNow) };
        importEvent.Contents.AddRange(contents.Select(ConvertToProto));
        RaiseEvent(importEvent);

        Logger.LogInformation($"Imported {contents.Count} contents");
    }

    public async Task<ContentStatistics> GetStatisticsAsync()
    {
        var activeContents = State.Contents.Values.Where(c => c.IsActive).ToList();

        return new ContentStatistics
        {
            TotalContents = State.Contents.Count,
            ActiveContents = activeContents.Count,
            LanguageDistribution = activeContents
                .SelectMany(c => c.LocalizedContents.Keys)
                .GroupBy(lang => lang)
                .ToDictionary(g => g.Key, g => g.Count()),
            LastSelection = State.LastSelection?.ToDateTime() ?? DateTime.MinValue,
            TotalSelections = State.SelectionCount
        };
    }

    public async Task RegisterTimezoneGuidMappingAsync(Guid timezoneGuid, string timezoneId)
    {
        var guidString = timezoneGuid.ToString();
        if (!State.TimezoneGuidMappings.ContainsKey(guidString))
        {
            // ✅ Use Event Sourcing for timezone mapping
            RaiseEvent(new UpdateTimezoneGuidMappingEvent
            {
                TimezoneGuid = guidString,
                TimezoneId = timezoneId
            });
            
            Logger.LogInformation("Registered timezone GUID mapping: {Guid} -> {TimezoneId}", timezoneGuid,
                timezoneId);
        }
    }

    public async Task<string?> GetTimezoneFromGuidAsync(Guid timezoneGuid)
    {
        return State.TimezoneGuidMappings.TryGetValue(timezoneGuid.ToString(), out var timezoneId) ? timezoneId : null;
    }

    public async Task<Dictionary<Guid, string>> GetAllTimezoneMappingsAsync()
    {
        return State.TimezoneGuidMappings.ToDictionary(
            kvp => Guid.Parse(kvp.Key),
            kvp => kvp.Value
        );
    }

    /// <summary>
    /// Pre-register common timezone mappings to prevent orphaned DailyPushCoordinatorGAgent grains
    /// This solves the chicken-egg problem where auto-activated grains can't find their timezone
    /// </summary>
    private async Task EnsureCommonTimezoneMappingsAsync()
    {
        var commonTimezones = new[]
        {
            "UTC",
            "Asia/Shanghai",
            "Asia/Tokyo",
            "Europe/London",
            "Europe/Rome",
            "Europe/Paris",
            "America/New_York",
            "America/Los_Angeles",
            "Australia/Sydney"
        };

        var registeredCount = 0;
        foreach (var timezone in commonTimezones)
        {
            var timezoneGuid = DailyPushConstants.TimezoneToGuid(timezone);
            var guidString = timezoneGuid.ToString();
            if (!State.TimezoneGuidMappings.ContainsKey(guidString))
            {
                // ✅ Use Event Sourcing for timezone mapping
                RaiseEvent(new UpdateTimezoneGuidMappingEvent
                {
                    TimezoneGuid = guidString,
                    TimezoneId = timezone
                });
                registeredCount++;
                Logger.LogDebug("Pre-registered timezone mapping: {Guid} -> {TimezoneId}", timezoneGuid, timezone);
            }
        }

        if (registeredCount > 0)
        {
            Logger.LogInformation("✅ Pre-registered {Count} common timezone mappings to prevent orphaned grains",
                registeredCount);
        }
        else
        {
            Logger.LogDebug("🔄 All common timezone mappings already exist ({Count} total mappings)",
                State.TimezoneGuidMappings.Count);
        }
    }
}