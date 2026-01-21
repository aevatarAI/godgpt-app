using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.Twitter;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// TwitterAuth State converter
/// </summary>
public class TwitterAuthStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new TwitterAuthStateProto();

        var newState = new TwitterAuthStateProto();

        if (oldState.TryGetValue("TwitterId", out var twitterIdObj))
            newState.TwitterId = ConvertToString(twitterIdObj);

        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        if (oldState.TryGetValue("ScreenName", out var screenNameObj))
            newState.ScreenName = ConvertToString(screenNameObj);

        if (oldState.TryGetValue("AccessToken", out var accessTokenObj))
            newState.AccessToken = ConvertToString(accessTokenObj);

        if (oldState.TryGetValue("AccessTokenSecret", out var accessTokenSecretObj))
            newState.AccessTokenSecret = ConvertToString(accessTokenSecretObj);

        if (oldState.TryGetValue("DisplayName", out var displayNameObj))
            newState.DisplayName = ConvertToString(displayNameObj);

        if (oldState.TryGetValue("ProfileImageUrl", out var profileImageUrlObj))
            newState.ProfileImageUrl = ConvertToString(profileImageUrlObj);

        if (oldState.TryGetValue("AuthTime", out var authTimeObj))
        {
            var dt = ConvertToDateTime(authTimeObj);
            if (dt.HasValue)
                newState.AuthTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("TokenExpiresAt", out var tokenExpiresAtObj))
        {
            var dt = ConvertToDateTime(tokenExpiresAtObj);
            if (dt.HasValue)
                newState.TokenExpiresAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
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
