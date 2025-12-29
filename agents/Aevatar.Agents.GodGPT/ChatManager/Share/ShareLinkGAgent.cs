using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Agents.ChatManager.Share;

/// <summary>
/// Share Link Agent - stores shared chat content
/// </summary>
public class ShareLinkGAgent : GAgentBase<ShareLinkProto>, IShareLinkGAgent
{
    public ShareLinkGAgent() : base() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"ShareLink: User={State.UserId}, Session={State.SessionId}");
    }

    public Task SaveShareContentAsync(ShareLinkProto shareLink)
    {
        State.UserId = shareLink.UserId;
        State.SessionId = shareLink.SessionId;
        State.Messages.Clear();
        State.Messages.AddRange(shareLink.Messages);
        State.CreateTime = Timestamp.FromDateTime(DateTime.UtcNow);
        
        Logger.LogDebug("[ShareLinkGAgent] Saved share content for session {SessionId}", shareLink.SessionId);
        return Task.CompletedTask;
    }

    public Task<ShareLinkProto> GetShareContentAsync()
    {
        var result = new ShareLinkProto
        {
            UserId = State.UserId,
            SessionId = State.SessionId,
            CreateTime = State.CreateTime
        };
        result.Messages.AddRange(State.Messages);
        
        return Task.FromResult(result);
    }
}

