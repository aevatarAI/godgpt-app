using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// ShareState converter - converts old Orleans ShareState grain state to new ShareLinkProto
/// </summary>
public class ShareStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new ShareLinkProto();

        var newState = new ShareLinkProto();

        // UserId: Guid -> string
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        // SessionId: Guid -> string
        if (oldState.TryGetValue("SessionId", out var sessionIdObj))
        {
            var sessionIdStr = ConvertToString(sessionIdObj);
            if (!string.IsNullOrEmpty(sessionIdStr) && Guid.TryParse(sessionIdStr, out var guid))
                newState.SessionId = guid.ToString("D");
        }

        // Messages: List<ChatMessage> -> repeated ChatMessageProto
        // Note: This is a simplified conversion - actual message structure may need more detailed mapping
        if (oldState.TryGetValue("Messages", out var messagesObj) && messagesObj != null)
        {
            var messageList = ConvertToList(messagesObj);
            if (messageList != null)
            {
                foreach (var msgObj in messageList)
                {
                    // Basic message conversion - may need more fields based on actual structure
                    var chatMessage = ConvertChatMessage(msgObj);
                    if (chatMessage != null)
                        newState.Messages.Add(chatMessage);
                }
            }
        }

        // CreateTime: DateTime -> Timestamp
        if (oldState.TryGetValue("CreateTime", out var createTimeObj))
        {
            var dt = ConvertToDateTime(createTimeObj);
            if (dt.HasValue)
                newState.CreateTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }
        else
        {
            // Default to current time if not provided
            newState.CreateTime = Timestamp.FromDateTime(DateTime.UtcNow);
        }

        return newState;
    }

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) 
            return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
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

    private static List<object>? ConvertToList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<object> list) return list;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var result = new List<object>();
            foreach (var item in je.EnumerateArray())
                result.Add(item);
            return result;
        }
        return null;
    }

    private static Aevatar.Agents.GodGPT.Protos.GodChat.ChatMessageProto? ConvertChatMessage(object? msgObj)
    {
        if (msgObj == null) return null;

        var msg = new Aevatar.Agents.GodGPT.Protos.GodChat.ChatMessageProto();

        if (msgObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("Content", out var content))
                msg.Content = ConvertToString(content);
            if (je.TryGetProperty("Role", out var role))
                msg.Role = ConvertToString(role);
            if (je.TryGetProperty("Timestamp", out var timestamp))
            {
                var dt = ConvertToDateTime(timestamp);
                if (dt.HasValue)
                    msg.Timestamp = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
        }

        return msg;
    }
}
