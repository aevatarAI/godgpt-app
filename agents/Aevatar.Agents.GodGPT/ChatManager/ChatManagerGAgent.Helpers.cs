using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Payment.Agents;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    #region Agent Factory Methods
    
    private async Task<IUserInfoCollectionGAgent> GetUserInfoCollectionAgentAsync(string userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId);
        return actor.As<IUserInfoCollectionGAgent>();
    }
    
    private async Task<IUserQuotaGAgent> GetUserQuotaAgentAsync(string userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId);
        return actor.As<IUserQuotaGAgent>();
    }
    
    private async Task<IPaymentIndexGAgent> GetPaymentIndexAgentAsync(string userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<PaymentIndexGAgent>(userId);
        return actor.As<IPaymentIndexGAgent>();
    }
    
    private async Task<IPaymentRecordGAgent> GetPaymentRecordAgentAsync(string paymentId)
    {
        // Convert paymentId to stable Agent ID (same logic as PaymentService)
        var agentId = PaymentIdHelper.ToAgentIdString(paymentId);
        var actor = await _actorFactory.CreateGAgentActorAsync<PaymentRecordGAgent>(agentId);
        return actor.As<IPaymentRecordGAgent>();
    }
    
    #endregion
    
    private async Task<IConfigurationGAgent> GetConfigurationAsync()
    {
        if (_configurationAgentInterface == null)
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(
                CommonHelper.GetSessionManagerConfigurationId().ToString());
            _configurationAgentInterface = actor.As<IConfigurationGAgent>();
        }
        return _configurationAgentInterface;
    }

    private async Task<IInviteCodeGAgent> GetInviteCodeAgentAsync(string codeGrainId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InviteCodeGAgent>(codeGrainId);
        return actor.As<IInviteCodeGAgent>();
    }

    private async Task<IInvitationGAgent> GetInvitationAgentAsync(string userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(userId);
        return actor.As<IInvitationGAgent>();
    }

    /// <summary>
    /// Extract raw Guid from Agent ID (may be in "AgentType:Guid" format)
    /// </summary>
    private static Guid ExtractGuidFromId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return Guid.Empty;
            
        // Check if it's in "AgentType:Guid" format
        var colonIndex = id.LastIndexOf(':');
        if (colonIndex >= 0 && colonIndex < id.Length - 1)
        {
            var guidPart = id.Substring(colonIndex + 1);
            if (Guid.TryParse(guidPart, out var guid))
                return guid;
        }
        
        // Try parsing the whole string as a Guid
        if (Guid.TryParse(id, out var result))
            return result;
            
        return Guid.Empty;
    }
    
    /// <summary>
    /// Get role-specific prompt from configuration based on role name
    /// </summary>
    /// <param name="roleName">The name of the role (e.g., "Doctor", "Teacher")</param>
    /// <returns>Role-specific prompt text or empty string if not found</returns>
    private string GetRolePrompt(string roleName)
    {
        var rolePrompt = RolePromptOptions?.RolePrompts.GetValueOrDefault(roleName, string.Empty) ?? string.Empty;

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
}