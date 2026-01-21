using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.Twitter;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// TwitterAuth State converter - converts old TwitterAuthGAgent state to godgpt TwitterAuthState
/// </summary>
public class TwitterAuthStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new TwitterAuthState();

        var newState = new TwitterAuthState();

        // Map TwitterId -> twitter_user_id
        if (oldState.TryGetValue("TwitterId", out var twitterIdObj))
            newState.TwitterUserId = ConvertToString(twitterIdObj);

        // Map UserId -> user_id
        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        // Map ScreenName -> username
        if (oldState.TryGetValue("ScreenName", out var screenNameObj))
            newState.Username = ConvertToString(screenNameObj);

        // Map AccessToken -> access_token
        if (oldState.TryGetValue("AccessToken", out var accessTokenObj))
            newState.AccessToken = ConvertToString(accessTokenObj);

        // AccessTokenSecret is not in godgpt TwitterAuthState (OAuth2 uses refresh_token instead)
        // RefreshToken is not in old state, leave empty

        // Map TokenExpiresAt -> token_expires_at
        if (oldState.TryGetValue("TokenExpiresAt", out var tokenExpiresAtObj))
        {
            var dt = ConvertToDateTime(tokenExpiresAtObj);
            if (dt.HasValue)
                newState.TokenExpiresAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // Map ProfileImageUrl -> profile_image_url
        if (oldState.TryGetValue("ProfileImageUrl", out var profileImageUrlObj))
            newState.ProfileImageUrl = ConvertToString(profileImageUrlObj);

        // Set is_bound based on whether we have tokens
        newState.IsBound = !string.IsNullOrEmpty(newState.AccessToken);

        // AuthTime, DisplayName, AccessTokenSecret are not in godgpt version - skip

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
