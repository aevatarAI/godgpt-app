using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.UserInfoCollection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// UserInfoCollection State converter
/// </summary>
public class UserInfoCollectionStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        if (oldState == null)
            return new UserInfoCollectionState();

        var newState = new UserInfoCollectionState();

        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        // IsInitialized: bool
        // IMPORTANT: If any meaningful data exists, mark as initialized
        // to prevent GetUserInfoCollectionAsync/GetUserInfoDisplayAsync returning null
        var hasMeaningfulData = oldState.ContainsKey("UserId") || 
                                oldState.ContainsKey("Gender") ||
                                oldState.ContainsKey("FirstName") ||
                                oldState.ContainsKey("Country");
        if (hasMeaningfulData)
        {
            newState.IsInitialized = true;
        }
        else if (oldState.TryGetValue("IsInitialized", out var isInitializedObj))
        {
            newState.IsInitialized = ConvertToBool(isInitializedObj);
        }

        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("LastUpdated", out var lastUpdatedObj))
        {
            var dt = ConvertToDateTime(lastUpdatedObj);
            if (dt.HasValue)
                newState.LastUpdated = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("Gender", out var genderObj))
            newState.Gender = ConvertToInt32(genderObj);

        if (oldState.TryGetValue("FirstName", out var firstNameObj))
            newState.FirstName = ConvertToString(firstNameObj);

        if (oldState.TryGetValue("LastName", out var lastNameObj))
            newState.LastName = ConvertToString(lastNameObj);

        if (oldState.TryGetValue("Country", out var countryObj))
            newState.Country = ConvertToString(countryObj);

        if (oldState.TryGetValue("City", out var cityObj))
            newState.City = ConvertToString(cityObj);

        if (oldState.TryGetValue("Day", out var dayObj))
            newState.Day = ConvertToInt32(dayObj);

        if (oldState.TryGetValue("Month", out var monthObj))
            newState.Month = ConvertToInt32(monthObj);

        if (oldState.TryGetValue("Year", out var yearObj))
            newState.Year = ConvertToInt32(yearObj);

        if (oldState.TryGetValue("Hour", out var hourObj))
        {
            var hour = ConvertToNullableInt32(hourObj);
            if (hour.HasValue)
                newState.Hour = hour.Value;
        }

        if (oldState.TryGetValue("Minute", out var minuteObj))
        {
            var minute = ConvertToNullableInt32(minuteObj);
            if (minute.HasValue)
                newState.Minute = minute.Value;
        }

        if (oldState.TryGetValue("SeekingInterests", out var seekingInterestsObj) && seekingInterestsObj != null)
        {
            var list = ConvertToStringList(seekingInterestsObj);
            if (list != null)
                newState.SeekingInterests.AddRange(list);
        }

        if (oldState.TryGetValue("SourceChannels", out var sourceChannelsObj) && sourceChannelsObj != null)
        {
            var list = ConvertToStringList(sourceChannelsObj);
            if (list != null)
                newState.SourceChannels.AddRange(list);
        }

        if (oldState.TryGetValue("SeekingInterestsCode", out var seekingInterestsCodeObj) && seekingInterestsCodeObj != null)
        {
            var list = ConvertToIntList(seekingInterestsCodeObj);
            if (list != null)
                newState.SeekingInterestsCode.AddRange(list);
        }

        if (oldState.TryGetValue("SourceChannelsCode", out var sourceChannelsCodeObj) && sourceChannelsCodeObj != null)
        {
            var list = ConvertToIntList(sourceChannelsCodeObj);
            if (list != null)
                newState.SourceChannelsCode.AddRange(list);
        }

        if (oldState.TryGetValue("FixState", out var fixStateObj))
            newState.FixState = ConvertToInt32(fixStateObj);

        return newState;
    }

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private static bool ConvertToBool(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (obj is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        if (bool.TryParse(obj.ToString(), out var result)) return result;
        return false;
    }

    private static int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }

    private static int? ConvertToNullableInt32(object? obj)
    {
        if (obj == null) return null;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Null) return null;
        var value = ConvertToInt32(obj);
        return value == 0 && obj is JsonElement jeZero && jeZero.ValueKind == JsonValueKind.Null ? null : value;
    }

    private static DateTime? ConvertToDateTime(object? obj)
    {
        if (obj == null) return null;
        if (obj is DateTime dt) return dt;
        if (obj is DateTimeOffset dto) return dto.DateTime;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(je.GetString(), out var dt2)) return dt2;
        }
        if (DateTime.TryParse(obj.ToString(), out var dt3)) return dt3;
        return null;
    }

    private static List<string>? ConvertToStringList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<string> list) return list;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var resultList = new List<string>();
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    resultList.Add(item.GetString() ?? string.Empty);
                else if (item.ValueKind != JsonValueKind.Null)
                    resultList.Add(item.ToString());
            }
            return resultList;
        }
        if (obj is IEnumerable<object?> objEnumerable)
        {
            return objEnumerable.Select(item => item?.ToString() ?? string.Empty).ToList();
        }
        return null;
    }

    private static List<int>? ConvertToIntList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<int> intList) return intList;
        if (obj is IEnumerable<int> intEnumerable) return intEnumerable.ToList();
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var resultList = new List<int>();
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
                    resultList.Add(number);
                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var parsed))
                    resultList.Add(parsed);
            }
            return resultList;
        }
        if (obj is IEnumerable<object?> objEnumerable)
        {
            var resultList = new List<int>();
            foreach (var item in objEnumerable)
            {
                if (item == null) continue;
                if (item is int i) resultList.Add(i);
                else if (int.TryParse(item.ToString(), out var parsed)) resultList.Add(parsed);
            }
            return resultList;
        }
        return null;
    }
}
