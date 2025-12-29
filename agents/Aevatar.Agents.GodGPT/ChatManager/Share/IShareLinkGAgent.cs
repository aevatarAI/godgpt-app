using Aevatar.Agents.GodGPT.Protos.ChatManager;

namespace Aevatar.Application.Grains.Agents.ChatManager.Share;

/// <summary>
/// Interface for Share Link Agent
/// </summary>
public interface IShareLinkGAgent : IGAgent
{
    Task SaveShareContentAsync(ShareLinkProto shareLink);
    Task<ShareLinkProto> GetShareContentAsync();
}

