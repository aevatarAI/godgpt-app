using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.Twitter;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Twitter Identity Binding GAgent - Maps Twitter ID to system user ID
/// Uses EventSourcing pattern for state management
/// </summary>
public class TwitterIdentityBindingGAgent : GAgentBase<TwitterIdentityBindingState>, ITwitterIdentityBindingGAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Twitter Identity Binding GAgent - Twitter: {State.TwitterUserId}");
    }

    /// <summary>
    /// Create or update binding between Twitter ID and system user ID
    /// </summary>
    public async Task<TwitterAuthResultProto> CreateOrUpdateBindingAsync(
        string twitterUserId, 
        Guid userId, 
        string twitterUsername, 
        string profileImageUrl)
    {
        try
        {
            var isNew = string.IsNullOrEmpty(State.UserId);
            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            
            if (isNew)
            {
                RaiseEvent(new TwitterIdentityBindingCreatedEvent
                {
                    TwitterUserId = twitterUserId,
                    UserId = userId.ToString(),
                    TwitterUsername = twitterUsername,
                    ProfileImageUrl = profileImageUrl,
                    CreatedAt = now
                });
            }
            else
            {
                RaiseEvent(new TwitterIdentityBindingUpdatedEvent
                {
                    TwitterUserId = twitterUserId,
                    UserId = userId.ToString(),
                    TwitterUsername = twitterUsername,
                    ProfileImageUrl = profileImageUrl,
                    UpdatedAt = now
                });
            }
            
            await ConfirmEventsAsync();
            
            Logger.LogInformation(
                "Twitter identity binding {Action}: Twitter={TwitterId}, User={UserId}", 
                isNew ? "created" : "updated",
                twitterUserId, 
                userId);
            
            return new TwitterAuthResultProto
            {
                Success = true,
                TwitterId = twitterUserId,
                Username = twitterUsername,
                BindStatus = true,
                ProfileImageUrl = profileImageUrl
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating/updating Twitter identity binding");
            return new TwitterAuthResultProto
            {
                Success = false,
                Error = "Failed to create/update binding"
            };
        }
    }

    /// <summary>
    /// Get the system user ID bound to a Twitter ID
    /// </summary>
    public Task<Guid?> GetUserIdAsync()
    {
        if (string.IsNullOrEmpty(State.UserId))
        {
            return Task.FromResult<Guid?>(null);
        }
        
        return Task.FromResult<Guid?>(Guid.Parse(State.UserId));
    }

    /// <summary>
    /// Get binding status
    /// </summary>
    public Task<TwitterBindStatusProto> GetBindStatusAsync()
    {
        return Task.FromResult(new TwitterBindStatusProto
        {
            IsBound = !string.IsNullOrEmpty(State.UserId),
            TwitterId = State.TwitterUserId ?? string.Empty,
            Username = State.TwitterUsername ?? string.Empty,
            ProfileImageUrl = State.ProfileImageUrl ?? string.Empty
        });
    }

    /// <summary>
    /// Handle state transitions for all events (EventSourcing pattern)
    /// </summary>
    protected override void TransitionState(TwitterIdentityBindingState state, IMessage @event)
    {
        switch (@event)
        {
            case TwitterIdentityBindingCreatedEvent created:
                state.TwitterUserId = created.TwitterUserId;
                state.UserId = created.UserId;
                state.TwitterUsername = created.TwitterUsername;
                state.ProfileImageUrl = created.ProfileImageUrl;
                state.CreatedAt = created.CreatedAt;
                state.UpdatedAt = created.CreatedAt;
                break;
                
            case TwitterIdentityBindingUpdatedEvent updated:
                state.TwitterUserId = updated.TwitterUserId;
                state.UserId = updated.UserId;
                state.TwitterUsername = updated.TwitterUsername;
                state.ProfileImageUrl = updated.ProfileImageUrl;
                state.UpdatedAt = updated.UpdatedAt;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", @event.GetType().Name);
                break;
        }
    }
}
