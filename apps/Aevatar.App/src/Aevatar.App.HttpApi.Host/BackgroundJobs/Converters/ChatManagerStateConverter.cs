using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.UserDevice;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// ChatManager State converter
/// Also extracts UserDevicesV2 to generate UserDeviceState for UserDeviceGAgent
/// </summary>
public class ChatManagerStateConverter : IStateConverter
{
    /// <summary>
    /// Additional UserDeviceState records extracted from UserDevicesV2
    /// Key: AgentId (UserDeviceGAgent:{userId}), Value: UserDeviceState
    /// </summary>
    public List<(string AgentId, IMessage State)> AdditionalUserDeviceRecords { get; } = new();

    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        // Clear previous records
        AdditionalUserDeviceRecords.Clear();
        
        if (oldState == null)
            return new ChatManagerStateProto();

        var newState = new ChatManagerStateProto();

        // SessionInfoList: List<SessionInfo> -> repeated SessionInfoProto
        if (oldState.TryGetValue("SessionInfoList", out var sessionListObj) && sessionListObj != null)
        {
            var sessionList = ConvertToList(sessionListObj);
            if (sessionList != null)
            {
                foreach (var sessionObj in sessionList)
                {
                    var session = ConvertSessionInfo(sessionObj);
                    if (session != null)
                        newState.SessionInfoList.Add(session);
                }
            }
        }

        // UserId: Guid -> string
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        // MaxSession: int
        if (oldState.TryGetValue("MaxSession", out var maxSessionObj))
            newState.MaxSession = ConvertToInt32(maxSessionObj);

        // Gender: string
        if (oldState.TryGetValue("Gender", out var genderObj))
            newState.Gender = ConvertToString(genderObj);

        // BirthDate: DateTime -> Timestamp
        if (oldState.TryGetValue("BirthDate", out var birthDateObj))
        {
            var dt = ConvertToDateTime(birthDateObj);
            if (dt.HasValue)
                newState.BirthDate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // BirthPlace: string
        if (oldState.TryGetValue("BirthPlace", out var birthPlaceObj))
            newState.BirthPlace = ConvertToString(birthPlaceObj);

        // FullName: string
        if (oldState.TryGetValue("FullName", out var fullNameObj))
            newState.FullName = ConvertToString(fullNameObj);

        // MaxShareCount: int
        if (oldState.TryGetValue("MaxShareCount", out var maxShareCountObj))
            newState.MaxShareCount = ConvertToInt32(maxShareCountObj);

        // CurrentShareCount: int
        if (oldState.TryGetValue("CurrentShareCount", out var currentShareCountObj))
            newState.CurrentShareCount = ConvertToInt32(currentShareCountObj);

        // IsFirstConversation: bool?
        if (oldState.TryGetValue("IsFirstConversation", out var isFirstConversationObj))
        {
            var value = ConvertToBool(isFirstConversationObj);
            newState.IsFirstConversation = value;
        }

        // RegisteredAtUtc: DateTime? -> optional Timestamp
        if (oldState.TryGetValue("RegisteredAtUtc", out var registeredAtObj))
        {
            var dt = ConvertToDateTime(registeredAtObj);
            if (dt.HasValue)
                newState.RegisteredAtUtc = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // InviterId: Guid? -> optional string
        if (oldState.TryGetValue("InviterId", out var inviterIdObj))
        {
            var inviterIdStr = ConvertToString(inviterIdObj);
            if (!string.IsNullOrEmpty(inviterIdStr) && Guid.TryParse(inviterIdStr, out _))
                newState.InviterId = inviterIdStr;
        }

        // VoiceLanguage: int
        if (oldState.TryGetValue("VoiceLanguage", out var voiceLanguageObj))
            newState.VoiceLanguage = ConvertToInt32(voiceLanguageObj);

        // StateVersion: int
        if (oldState.TryGetValue("StateVersion", out var stateVersionObj))
            newState.StateVersion = ConvertToInt32(stateVersionObj);

        // Extract UserDevicesV2 and select best device for UserDeviceState
        // Only process V2 devices, ignore V1 (UserDevices)
        if (oldState.TryGetValue("UserDevicesV2", out var devicesV2Obj) && devicesV2Obj != null)
        {
            var bestDevice = SelectBestDeviceV2(devicesV2Obj);
            if (bestDevice != null && !string.IsNullOrEmpty(newState.UserId))
            {
                var userDeviceState = ConvertToUserDeviceState(bestDevice, newState.UserId);
                if (userDeviceState != null)
                {
                    var agentId = $"UserDeviceGAgent:{newState.UserId}";
                    AdditionalUserDeviceRecords.Add((agentId, userDeviceState));
                }
            }
        }

        return newState;
    }

    /// <summary>
    /// Select best device from UserDevicesV2
    /// Rule: PushEnabled == true, OrderByDescending(LastTokenUpdate), FirstOrDefault
    /// </summary>
    private Dictionary<string, object?>? SelectBestDeviceV2(object? devicesObj)
    {
        var devicesDict = ConvertToDictionary(devicesObj);
        if (devicesDict == null || devicesDict.Count == 0)
            return null;

        // Parse all devices and filter by PushEnabled
        var enabledDevices = new List<(Dictionary<string, object?> Device, DateTime LastTokenUpdate)>();
        
        foreach (var kvp in devicesDict)
        {
            var deviceDict = ConvertToDictionary(kvp.Value);
            if (deviceDict == null)
                continue;

            // Check PushEnabled
            var pushEnabled = false;
            if (deviceDict.TryGetValue("PushEnabled", out var pushEnabledObj))
                pushEnabled = ConvertToBool(pushEnabledObj);

            if (!pushEnabled)
                continue;

            // Get LastTokenUpdate for sorting
            var lastTokenUpdate = DateTime.MinValue;
            if (deviceDict.TryGetValue("LastTokenUpdate", out var lastTokenUpdateObj))
            {
                var dt = ConvertToDateTime(lastTokenUpdateObj);
                if (dt.HasValue)
                    lastTokenUpdate = dt.Value;
            }

            enabledDevices.Add((deviceDict, lastTokenUpdate));
        }

        if (enabledDevices.Count == 0)
            return null;

        // Sort by LastTokenUpdate descending and take first
        var bestDevice = enabledDevices
            .OrderByDescending(d => d.LastTokenUpdate)
            .First()
            .Device;

        return bestDevice;
    }

    /// <summary>
    /// Convert device dictionary to UserDeviceState
    /// </summary>
    private UserDeviceState? ConvertToUserDeviceState(Dictionary<string, object?> deviceDict, string userId)
    {
        var state = new UserDeviceState
        {
            UserId = userId
        };

        // DeviceId
        if (deviceDict.TryGetValue("DeviceId", out var deviceIdObj))
            state.DeviceId = ConvertToString(deviceIdObj);

        // PushToken
        if (deviceDict.TryGetValue("PushToken", out var pushTokenObj))
            state.PushToken = ConvertToString(pushTokenObj);

        // TimeZoneId
        if (deviceDict.TryGetValue("TimeZoneId", out var timeZoneIdObj))
            state.TimeZoneId = ConvertToString(timeZoneIdObj);

        // PushEnabled
        if (deviceDict.TryGetValue("PushEnabled", out var pushEnabledObj))
            state.PushEnabled = ConvertToBool(pushEnabledObj);

        // LastTokenUpdate -> token_updated_at
        if (deviceDict.TryGetValue("LastTokenUpdate", out var lastTokenUpdateObj))
        {
            var dt = ConvertToDateTime(lastTokenUpdateObj);
            if (dt.HasValue)
                state.TokenUpdatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // LastActiveAt -> last_active_at
        if (deviceDict.TryGetValue("LastActiveAt", out var lastActiveAtObj))
        {
            var dt = ConvertToDateTime(lastActiveAtObj);
            if (dt.HasValue)
                state.LastActiveAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // Platform
        if (deviceDict.TryGetValue("Platform", out var platformObj))
            state.Platform = ConvertToString(platformObj);

        // AppVersion
        if (deviceDict.TryGetValue("AppVersion", out var appVersionObj))
            state.AppVersion = ConvertToString(appVersionObj);

        // ConsecutiveFailures > 0 -> token_invalid = true
        if (deviceDict.TryGetValue("ConsecutiveFailures", out var failuresObj))
        {
            var failures = ConvertToInt32(failuresObj);
            state.TokenInvalid = failures > 0;
        }

        // PushLanguage -> language
        if (deviceDict.TryGetValue("PushLanguage", out var pushLanguageObj))
            state.Language = ConvertToString(pushLanguageObj);

        return state;
    }

    private SessionInfoProto? ConvertSessionInfo(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new SessionInfoProto();

        if (dict.TryGetValue("SessionId", out var sessionIdObj))
        {
            var sessionIdStr = ConvertToString(sessionIdObj);
            if (!string.IsNullOrEmpty(sessionIdStr) && Guid.TryParse(sessionIdStr, out var guid))
                info.SessionId = guid.ToString("D");
        }

        if (dict.TryGetValue("Title", out var titleObj))
            info.Title = ConvertToString(titleObj);

        if (dict.TryGetValue("CreateAt", out var createAtObj))
        {
            var dt = ConvertToDateTime(createAtObj);
            if (dt.HasValue)
                info.CreateAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("Guider", out var guiderObj))
        {
            var guiderStr = ConvertToString(guiderObj);
            if (!string.IsNullOrEmpty(guiderStr))
                info.Guider = guiderStr;
        }

        if (dict.TryGetValue("ShareIds", out var shareIdsObj) && shareIdsObj != null)
        {
            var ids = ConvertToStringList(shareIdsObj);
            if (ids != null)
            {
                foreach (var id in ids)
                    info.ShareIds.Add(id);
            }
        }

        return info;
    }

    private string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private bool ConvertToBool(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (obj is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        if (bool.TryParse(obj.ToString(), out var result)) return result;
        return false;
    }

    private int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }

    private DateTime? ConvertToDateTime(object? obj)
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

    private Dictionary<string, object>? ConvertToDictionary(object? obj)
    {
        if (obj == null) return null;
        if (obj is Dictionary<string, object> dict) return dict;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
        return null;
    }

    private List<object>? ConvertToList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<object> existingList) return existingList;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var newList = new List<object>();
            foreach (var item in je.EnumerateArray())
                newList.Add(item);
            return newList;
        }
        return null;
    }

    private List<string>? ConvertToStringList(object? obj)
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
                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Guid", out var guidProp))
                    resultList.Add(guidProp.GetString() ?? string.Empty);
            }
            return resultList;
        }
        return null;
    }
}
