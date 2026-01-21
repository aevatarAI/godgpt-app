using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.Twitter;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// TwitterIdentityBinding State converter - converts from old system to godgpt TwitterIdentityBindingState
/// </summary>
public class TwitterIdentityBindingStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new TwitterIdentityBindingState();

        var newState = new TwitterIdentityBindingState();

        // Map old field names to new godgpt field names
        if (oldState.TryGetValue("TwitterId", out var twitterIdObj))
            newState.TwitterUserId = ConvertToString(twitterIdObj);

        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        if (oldState.TryGetValue("ScreenName", out var screenNameObj))
            newState.TwitterUsername = ConvertToString(screenNameObj);

        // DisplayName is not in godgpt TwitterIdentityBindingState

        if (oldState.TryGetValue("ProfileImageUrl", out var profileImageUrlObj))
            newState.ProfileImageUrl = ConvertToString(profileImageUrlObj);

        if (oldState.TryGetValue("BoundAt", out var boundAtObj))
        {
            var dt = ConvertToDateTime(boundAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("LastUsedAt", out var lastUsedAtObj))
        {
            var dt = ConvertToDateTime(lastUsedAtObj);
            if (dt.HasValue)
                newState.UpdatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }
        else if (oldState.TryGetValue("BoundAt", out var boundAtObj2))
        {
            // If no LastUsedAt, use BoundAt for UpdatedAt
            var dt = ConvertToDateTime(boundAtObj2);
            if (dt.HasValue)
                newState.UpdatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
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
