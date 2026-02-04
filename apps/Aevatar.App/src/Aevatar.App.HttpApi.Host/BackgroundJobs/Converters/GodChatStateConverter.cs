using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// GodChat State converter
/// </summary>
public class GodChatStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new GodChatStateProto();

        var newState = new GodChatStateProto();

        // ChatHistory: List<ChatMessage> -> repeated ChatMessageProto
        if (oldState.TryGetValue("ChatHistory", out var chatHistoryObj) && chatHistoryObj != null)
        {
            var chatList = ConvertToList(chatHistoryObj);
            if (chatList != null)
            {
                foreach (var chatObj in chatList)
                {
                    var chatMessage = ConvertChatMessage(chatObj);
                    if (chatMessage != null)
                        newState.ChatHistory.Add(chatMessage);
                }
            }
        }

        // MaxHistoryCount: int
        if (oldState.TryGetValue("MaxHistoryCount", out var maxHistoryCountObj))
            newState.MaxHistoryCount = ConvertToInt32(maxHistoryCountObj);

        // PromptTemplate: string?
        if (oldState.TryGetValue("PromptTemplate", out var promptTemplateObj))
        {
            var promptStr = ConvertToString(promptTemplateObj);
            if (!string.IsNullOrEmpty(promptStr))
                newState.PromptTemplate = promptStr;
        }

        // UserProfile: UserProfileProto?
        if (oldState.TryGetValue("UserProfile", out var userProfileObj))
        {
            var userProfile = ConvertUserProfile(userProfileObj);
            if (userProfile != null)
                newState.UserProfile = userProfile;
        }

        // Title: string?
        if (oldState.TryGetValue("Title", out var titleObj))
        {
            var titleStr = ConvertToString(titleObj);
            if (!string.IsNullOrEmpty(titleStr))
                newState.Title = titleStr;
        }

        // ChatManagerGuid: Guid -> string
        if (oldState.TryGetValue("ChatManagerGuid", out var chatManagerGuidObj))
        {
            var guidStr = ConvertToString(chatManagerGuidObj);
            if (!string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out var guid))
                newState.ChatManagerGuid = guid.ToString("D");
        }

        // RegionProxies: SKIP migration - let GodChatGAgent create new proxies on demand
        // Old AIAgentStatusProxy states have low value (temporary availability flags)
        // and will be garbage after migration. New proxies will be auto-created.
        // See: GodChatGAgent.ProxyManagement.cs - InitializeRegionProxiesAsync()

        // FirstChatTime: DateTime? -> optional Timestamp
        if (oldState.TryGetValue("FirstChatTime", out var firstChatTimeObj))
        {
            var dt = ConvertToDateTime(firstChatTimeObj);
            if (dt.HasValue)
                newState.FirstChatTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // LastChatTime: DateTime? -> optional Timestamp
        if (oldState.TryGetValue("LastChatTime", out var lastChatTimeObj))
        {
            var dt = ConvertToDateTime(lastChatTimeObj);
            if (dt.HasValue)
                newState.LastChatTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // ChatMessageMetas: List<ChatMessageMeta> -> repeated ChatMessageMetaProto
        if (oldState.TryGetValue("ChatMessageMetas", out var chatMessageMetasObj) && chatMessageMetasObj != null)
        {
            var metaList = ConvertToList(chatMessageMetasObj);
            if (metaList != null)
            {
                foreach (var metaObj in metaList)
                {
                    var meta = ConvertChatMessageMeta(metaObj);
                    if (meta != null)
                        newState.ChatMessageMetas.Add(meta);
                }
            }
        }

        // ProxyInitStatuses: SKIP - related to RegionProxies which is also skipped
        // No proxies = no proxy init statuses to track

        return newState;
    }

    private ChatMessageProto? ConvertChatMessage(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var message = new ChatMessageProto();

        if (dict.TryGetValue("Id", out var idObj))
            message.Id = ConvertToString(idObj);

        if (dict.TryGetValue("Role", out var roleObj))
            message.Role = ConvertToString(roleObj);

        if (dict.TryGetValue("Content", out var contentObj))
            message.Content = ConvertToString(contentObj);

        if (dict.TryGetValue("Timestamp", out var timestampObj))
        {
            var dt = ConvertToDateTime(timestampObj);
            if (dt.HasValue)
                message.Timestamp = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("Metadata", out var metadataObj) && metadataObj != null)
        {
            var metadataDict = ConvertToDictionary(metadataObj);
            if (metadataDict != null)
            {
                foreach (var kvp in metadataDict)
                    message.Metadata[kvp.Key] = ConvertToString(kvp.Value);
            }
        }

        if (dict.TryGetValue("ChatRole", out var chatRoleObj))
            message.ChatRole = ConvertToInt32(chatRoleObj);

        if (dict.TryGetValue("ImageKeys", out var imageKeysObj) && imageKeysObj != null)
        {
            var keys = ConvertToStringList(imageKeysObj);
            if (keys != null)
            {
                foreach (var key in keys)
                    message.ImageKeys.Add(key);
            }
        }

        return message;
    }

    private UserProfileProto? ConvertUserProfile(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var profile = new UserProfileProto();

        if (dict.TryGetValue("Gender", out var genderObj))
            profile.Gender = ConvertToString(genderObj);

        if (dict.TryGetValue("BirthDate", out var birthDateObj))
        {
            var dt = ConvertToDateTime(birthDateObj);
            if (dt.HasValue)
                profile.BirthDate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("BirthPlace", out var birthPlaceObj))
            profile.BirthPlace = ConvertToString(birthPlaceObj);

        if (dict.TryGetValue("FullName", out var fullNameObj))
            profile.FullName = ConvertToString(fullNameObj);

        return profile;
    }

    private RegionProxiesEntryProto? ConvertRegionProxiesEntry(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var entry = new RegionProxiesEntryProto();

        if (dict.TryGetValue("Region", out var regionObj))
            entry.Region = ConvertToString(regionObj);

        if (dict.TryGetValue("ProxyIds", out var proxyIdsObj) && proxyIdsObj != null)
        {
            var ids = ConvertToStringList(proxyIdsObj);
            if (ids != null)
            {
                foreach (var id in ids)
                    entry.ProxyIds.Add(id);
            }
        }

        return entry;
    }

    private ChatMessageMetaProto? ConvertChatMessageMeta(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var meta = new ChatMessageMetaProto();

        if (dict.TryGetValue("IsVoiceMessage", out var isVoiceObj))
            meta.IsVoiceMessage = ConvertToBool(isVoiceObj);

        if (dict.TryGetValue("VoiceLanguage", out var voiceLanguageObj))
            meta.VoiceLanguage = ConvertToInt32(voiceLanguageObj);

        if (dict.TryGetValue("VoiceParseSuccess", out var parseSuccessObj))
            meta.VoiceParseSuccess = ConvertToBool(parseSuccessObj);

        if (dict.TryGetValue("VoiceParseErrorMessage", out var errorMsgObj))
        {
            var errorMsg = ConvertToString(errorMsgObj);
            if (!string.IsNullOrEmpty(errorMsg))
                meta.VoiceParseErrorMessage = errorMsg;
        }

        if (dict.TryGetValue("VoiceDurationSeconds", out var durationObj))
            meta.VoiceDurationSeconds = ConvertToDouble(durationObj);

        return meta;
    }

    private ProxyInitStatusEntryProto? ConvertProxyInitStatusEntry(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var entry = new ProxyInitStatusEntryProto();

        if (dict.TryGetValue("ProxyId", out var proxyIdObj))
        {
            var proxyIdStr = ConvertToString(proxyIdObj);
            if (!string.IsNullOrEmpty(proxyIdStr) && Guid.TryParse(proxyIdStr, out var guid))
                entry.ProxyId = guid.ToString("D");
        }

        if (dict.TryGetValue("Status", out var statusObj))
            entry.Status = ConvertToProxyInitStatus(statusObj);

        return entry;
    }

    private ProxyInitStatusProto ConvertToProxyInitStatus(object? obj)
    {
        if (obj == null) return ProxyInitStatusProto.ProxyInitStatusNotInitialized;
        if (obj is int i) return (ProxyInitStatusProto)i;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return (ProxyInitStatusProto)je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return (ProxyInitStatusProto)result;
        return ProxyInitStatusProto.ProxyInitStatusNotInitialized;
    }

    private double ConvertToDouble(object? obj)
    {
        if (obj == null) return 0.0;
        if (obj is double d) return d;
        if (obj is float f) return f;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetDouble();
        if (double.TryParse(obj.ToString(), out var result)) return result;
        return 0.0;
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
