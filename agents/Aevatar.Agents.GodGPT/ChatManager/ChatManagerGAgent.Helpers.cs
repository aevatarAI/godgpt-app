using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserQuota;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    #region Agent Factory Methods
    
    private async Task<IUserInfoCollectionGAgent> GetUserInfoCollectionAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId);
        return (IUserInfoCollectionGAgent)actor.GetAgent();
    }
    
    private async Task<IUserQuotaGAgent> GetUserQuotaAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId);
        return (IUserQuotaGAgent)actor.GetAgent();
    }
    
    #endregion
    
    private async Task<ConfigurationGAgent> GetConfigurationAsync()
    {
        if (_configurationAgent == null)
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(
                CommonHelper.GetSessionManagerConfigurationId());
            _configurationAgent = (ConfigurationGAgent)actor.GetAgent();
        }
        return _configurationAgent;
    }

    private async Task<IInviteCodeGAgent> GetInviteCodeAgentAsync(Guid codeGrainId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InviteCodeGAgent>(codeGrainId);
        return (IInviteCodeGAgent)actor.GetAgent();
    }

    private async Task<IInvitationGAgent> GetInvitationAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(userId);
        return (IInvitationGAgent)actor.GetAgent();
    }

    /// <summary>
    /// Get role-specific prompt from configuration based on role name
    /// </summary>
    /// <param name="roleName">The name of the role (e.g., "Doctor", "Teacher")</param>
    /// <returns>Role-specific prompt text or empty string if not found</returns>
    private string GetRolePrompt(string roleName)
    {
        try
        {
            var roleOptions =
                (_serviceProvider.GetService(typeof(IOptionsMonitor<RolePromptOptions>)) as
                    IOptionsMonitor<RolePromptOptions>)?.CurrentValue;
            var rolePrompt = roleOptions?.RolePrompts.GetValueOrDefault(roleName, string.Empty) ?? string.Empty;

            if (!string.IsNullOrEmpty(rolePrompt))
            {
                Logger.LogDebug($"[ChatGAgentManager][GetRolePrompt] Found role prompt for: {roleName}");
            }
            else
            {
                Logger.LogDebug($"[ChatGAgentManager][GetRolePrompt] No role prompt found for: {roleName}");
            }

            return rolePrompt;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[ChatGAgentManager][GetRolePrompt] Failed to get role prompt for role: {RoleName}",
                roleName);
            return string.Empty;
        }
    }
}