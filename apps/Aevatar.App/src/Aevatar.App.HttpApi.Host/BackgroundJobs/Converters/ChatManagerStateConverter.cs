using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// ChatManager State converter
/// </summary>
public class ChatManagerStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
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

        return newState;
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
