using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.GoogleAuth;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// GoogleIdentityBinding State converter
/// Converts old GoogleIdentityBindingGAgent state to new proto format
/// </summary>
public class GoogleIdentityBindingStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new GoogleIdentityBindingStateProto();

        var newState = new GoogleIdentityBindingStateProto();

        if (oldState.TryGetValue("GoogleId", out var googleIdObj))
            newState.GoogleId = ConvertToString(googleIdObj);

        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        if (oldState.TryGetValue("Email", out var emailObj))
            newState.Email = ConvertToString(emailObj);

        if (oldState.TryGetValue("DisplayName", out var displayNameObj))
            newState.DisplayName = ConvertToString(displayNameObj);

        if (oldState.TryGetValue("BoundAt", out var boundAtObj))
        {
            var dt = ConvertToDateTime(boundAtObj);
            if (dt.HasValue)
                newState.BoundAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("LastUsedAt", out var lastUsedAtObj))
        {
            var dt = ConvertToDateTime(lastUsedAtObj);
            if (dt.HasValue)
                newState.LastUsedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
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
}
