using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Agents.ChatManager.Share;

/// <summary>
/// Share Link Agent - stores shared chat content using Event Sourcing
/// </summary>
public class ShareLinkGAgent : GAgentBase<ShareLinkProto>, IShareLinkGAgent
{
    public ShareLinkGAgent() : base() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"ShareLink: User={State.UserId}, Session={State.SessionId}");
    }

    public async Task SaveShareContentAsync(ShareLinkProto shareLink)
    {
        var evt = new SaveShareContentEvent
        {
            UserId = shareLink.UserId,
            SessionId = shareLink.SessionId,
            CreateTime = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        evt.Messages.AddRange(shareLink.Messages);
        
        RaiseEvent(evt);
        await ConfirmEventsAsync();
        
        Logger.LogInformation(
            "[ShareLinkGAgent] Saved share content for session {SessionId}, MessageCount: {Count}",
            shareLink.SessionId, shareLink.Messages.Count);
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
        
        Logger.LogDebug(
            "[ShareLinkGAgent] GetShareContent - SessionId: {SessionId}, MessageCount: {Count}",
            State.SessionId, State.Messages.Count);
        
        return Task.FromResult(result);
    }

    protected override void TransitionState(ShareLinkProto state, IMessage @event)
    {
        switch (@event)
        {
            case SaveShareContentEvent saveEvt:
                state.UserId = saveEvt.UserId;
                state.SessionId = saveEvt.SessionId;
                state.Messages.Clear();
                state.Messages.AddRange(saveEvt.Messages);
                state.CreateTime = saveEvt.CreateTime;
                Logger.LogDebug(
                    "[ShareLinkGAgent][TransitionState] SaveShareContentEvent - SessionId: {SessionId}, MessageCount: {Count}",
                    saveEvt.SessionId, saveEvt.Messages.Count);
                break;
        }
    }
}

