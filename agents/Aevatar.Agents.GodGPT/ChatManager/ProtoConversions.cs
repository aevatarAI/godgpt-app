using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Application.Grains.Agents.ChatManager;

/// <summary>
/// Conversion helpers between C# DTOs and Protobuf messages
/// NOTE: VoiceLanguage and ChatRole conversions are in ChatManagerConversions.cs
/// </summary>
public static class ProtoConversions
{
    #region UserProfile Conversions
    
    /// <summary>
    /// Convert UserProfileDto to UserProfileResponseProto
    /// </summary>
    public static UserProfileResponseProto ToProto(this UserProfileDto dto)
    {
        var proto = new UserProfileResponseProto
        {
            Gender = dto.Gender ?? string.Empty,
            BirthDate = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.BirthDate, DateTimeKind.Utc)),
            BirthPlace = dto.BirthPlace ?? string.Empty,
            FullName = dto.FullName ?? string.Empty,
            Id = dto.Id.ToString(),
            VoiceLanguage = dto.VoiceLanguage.ToProtoInt()  // Uses ChatManagerConversions
        };
        
        // Credits
        if (dto.Credits != null)
        {
            proto.Credits = new CreditsInfoProto
            {
                IsInitialized = dto.Credits.IsInitialized,
                Credits = dto.Credits.Credits,
                ShouldShowToast = dto.Credits.ShouldShowToast
            };
        }
        
        // Subscription (already Proto type)
        if (dto.Subscription != null)
        {
            proto.Subscription = dto.Subscription;
        }
        
        // UltimateSubscription (already Proto type)
        if (dto.UltimateSubscription != null)
        {
            proto.UltimateSubscription = dto.UltimateSubscription;
        }
        
        // Optional fields
        if (dto.InviterId.HasValue)
        {
            proto.InviterId = dto.InviterId.Value.ToString();
        }
        
        if (dto.IsFirstConversation.HasValue)
        {
            proto.IsFirstConversation = dto.IsFirstConversation.Value;
        }
        
        return proto;
    }
    
    #endregion
}
