using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.DailyPush;
using Aevatar.Application.Grains.Agents.Anonymous;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Payment.Abstractions;
using NewPaymentPlatform = Aevatar.Payment.Abstractions.PaymentPlatform;
using Aevatar.Application.Grains.UserStatistics;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.App.Domain.Shared;
using Aevatar.Dtos;
using Aevatar.GAgents.AI.Abstractions;
using Aevatar.GAgents.AI.Options;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.Common.Options;
using Aevatar.Application.Constants;
using Aevatar.GodGPT.Dtos;
using Aevatar.Quantum;
using Aevatar.Anonymous;
using GodGPT.GAgents;
using GodGPT.GAgents.Awakening;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Aevatar.Agents.Abstractions;
using Orleans;
using Orleans.Runtime;
using Stripe;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace Aevatar.Service;

public interface IGodGPTService
{
    Task<Guid> CreateSessionAsync(Guid userId, string systemLLM, string prompt, string? guider = null,
        DateTime? userLocalTime = null);

    Task<Tuple<string, string>> ChatWithSessionAsync(Guid userId, Guid sessionId, string sysmLLM, string content,
        ExecutionPromptSettings promptSettings = null);

    Task<List<SessionInfoDto>> GetSessionListAsync(Guid userId);
    Task<List<ChatMessage>> GetSessionMessageListAsync(Guid userId, Guid sessionId);
    Task<Aevatar.Quantum.SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid userId, Guid sessionId);
    Task<Guid> DeleteSessionAsync(Guid userId, Guid sessionId);
    Task<Guid> RenameSessionAsync(Guid userId, Guid sessionId, string title);
    Task<List<SessionInfoDto>> SearchSessionsAsync(Guid userId, string keyword);

    Task<string> GetSystemPromptAsync();
    Task UpdateSystemPromptAsync(GodGPTConfigurationDto godGptConfigurationDto);

    Task<UserProfileDto> GetUserProfileAsync(Guid currentUserId);
    Task<Guid> SetUserProfileAsync(Guid currentUserId, SetUserProfileInput userProfileDto);
    Task<Guid> DeleteAccountAsync(Guid currentUserId);
    Task<CreateShareIdResponse> GenerateShareContentAsync(Guid currentUserId, CreateShareIdRequest request, GodGPTChatLanguage language = GodGPTChatLanguage.English);
    Task<List<ChatMessage>> GetShareMessageListAsync(string shareString, GodGPTChatLanguage language = GodGPTChatLanguage.English);
    Task UpdateShowToastAsync(Guid currentUserId);
    Task<GrainResultDto<int>> UpdateUserCreditsAsync(Guid currentUserId, UpdateUserCreditsInput input);
    Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(Guid currentUserId, UpdateUserSubscriptionsInput input);
    Task<GetInvitationInfoResponse> GetInvitationInfoAsync(Guid currentUserId);
    Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(Guid currentUserId,
        RedeemInviteCodeRequest redeemInviteCodeRequest);
    Task<CreateGuestSessionResponseDto> CreateGuestSessionAsync(string clientIp, string? guider = null);
    Task GuestChatAsync(string clientIp, string content, string chatId);
    Task<GuestChatLimitsResponseDto> GetGuestChatLimitsAsync(string clientIp);
    Task<bool> CanGuestChatAsync(string clientIp);
    Task<QuantumShareResponseDto> GetShareKeyWordWithAIAsync(Guid sessionId, string? content, string? region, SessionType sessionType, GodGPTChatLanguage language = GodGPTChatLanguage.English);

    Task<PagedResultDto<RewardHistoryDto>> GetCreditsHistoryAsync(Guid currentUserId,
        GetCreditsHistoryInput getCreditsHistoryInput);

    Task<bool> CheckIsManager(Guid? currentUserId);
    Task<UserProfileDto> SetVoiceLanguageAsync(Guid currentUserId, VoiceLanguageEnum voiceLanguage);

    /// <summary>
    /// Get today's awakening content for the user
    /// </summary>
    /// <param name="currentUserId">Current user ID</param>
    /// <param name="language">Voice language preference</param>
    /// <param name="region">Optional region parameter</param>
    /// <returns>Awakening content DTO</returns>
    Task<AwakeningContentDto?> GetTodayAwakeningAsync(Guid currentUserId, VoiceLanguageEnum language, string? region);

    Task<ExecuteActionResultDto> CanUploadImageAsync(Guid currentUserId,GodGPTChatLanguage language = GodGPTChatLanguage.English);
    
    /// <summary>
    /// Reset awakening state for testing purposes (Admin only)
    /// </summary>
    /// <param name="userId">User ID to reset awakening state for</param>
    /// <returns>True if reset was successful</returns>
    Task<bool> ResetAwakeningStateForTestingAsync(Guid userId);

    Task<AppRatingRecordDto> RecordAppRatingAsync(Guid currentUserId, RecordAppRatingInput input);
    Task<bool> CanUserRateAppAsync(Guid currentUserId, CanUserRateAppInput input);
    Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(Guid currentUserId, GetInvitationCodeTypeRequest input);
    Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid currentUserId, GenerateFreeTrialCodeRequest input);
    Task<BatchInfoDto> GetBatchInfoAsync(string batchId);
}

[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTService : ApplicationService, IGodGPTService
{
    private readonly IClusterClient _clusterClient;
    private readonly IGAgentFactory _agentFactory;
    private readonly ILogger<GodGPTService> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly IOptionsMonitor<ManagerOptions> _managerOptions;
    private readonly ILocalizationService _localizationService;
    private readonly IPaymentService _paymentService;

    public GodGPTService(
        IClusterClient clusterClient, 
        IGAgentFactory agentFactory,
        ILogger<GodGPTService> logger, 
        IOptionsMonitor<StripeOptions> stripeOptions,
        IOptionsMonitor<ManagerOptions> managerOptions, 
        ILocalizationService localizationService,
        IPaymentService paymentService)
    {
        _clusterClient = clusterClient;
        _agentFactory = agentFactory;
        _logger = logger;
        _stripeOptions = stripeOptions;
        _managerOptions = managerOptions;
        _localizationService = localizationService;
        _paymentService = paymentService;
    }
    
    

    public async Task<Guid> CreateSessionAsync(Guid userId, string systemLLM, string prompt, string? guider = null,
        DateTime? userLocalTime = null)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.CreateSessionAsync(systemLLM, prompt, null, guider, userLocalTime);
    }

    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid userId, Guid sessionId, string sysmLLM,
        string content,
        ExecutionPromptSettings promptSettings = null)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.ChatWithSessionAsync(sessionId, sysmLLM, content, promptSettings);
    }

    public async Task<List<SessionInfoDto>> GetSessionListAsync(Guid userId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.GetSessionListAsync();
    }

    public async Task<List<ChatMessage>> GetSessionMessageListAsync(Guid userId, Guid sessionId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.GetSessionMessageListAsync(sessionId);
    }

    public async Task<Aevatar.Quantum.SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid userId, Guid sessionId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        var grainsResult = await manager.GetSessionCreationInfoAsync(sessionId);
        
        if (grainsResult != null)
        {
            return new Aevatar.Quantum.SessionCreationInfoDto
            {
                SessionId = grainsResult.SessionId,
                Title = grainsResult.Title,
                CreateAt = grainsResult.CreateAt,
                Guider = grainsResult.Guider
            };
        }
        
        return null;
    }

    public async Task<Guid> DeleteSessionAsync(Guid userId, Guid sessionId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.DeleteSessionAsync(sessionId);
    }

    public async Task<Guid> RenameSessionAsync(Guid userId, Guid sessionId, string title)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.RenameSessionAsync(sessionId, title);
    }

    public async Task<List<SessionInfoDto>> SearchSessionsAsync(Guid userId, string keyword)
    {
        // Input validation according to downstream team requirements
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<SessionInfoDto>(); // Return empty list for empty keyword
        }

        // Length limit validation
        if (keyword.Length > 200)
        {
            _logger.LogWarning($"Search keyword too long: {keyword.Length} characters");
            return new List<SessionInfoDto>();
        }

        try
        {
            var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
            return await manager.SearchSessionsAsync(keyword.Trim(), 1000);
        }
        catch (Exception ex)
        {
            // Error handling according to downstream team requirements
            _logger.LogError(ex, $"Search sessions failed for keyword: {keyword}");
            return new List<SessionInfoDto>();
        }
    }

    public Task<string> GetSystemPromptAsync()
    {
        var configurationAgent =
            _agentFactory.CreateGAgent<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
        return Task.FromResult(configurationAgent.GetPrompt());
    }

    public Task UpdateSystemPromptAsync(GodGPTConfigurationDto godGptConfigurationDto)
    {
        var configurationAgent =
            _agentFactory.CreateGAgent<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
        return configurationAgent.UpdateSystemPromptAsync(godGptConfigurationDto.SystemPrompt);
    }

    public async Task<UserProfileDto> GetUserProfileAsync(Guid currentUserId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(currentUserId);
        return await manager.GetUserProfileAsync();
    }

    public async Task<Guid> SetUserProfileAsync(Guid currentUserId, SetUserProfileInput userProfileDto)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(currentUserId);
        return await manager.SetUserProfileAsync(userProfileDto.Gender, userProfileDto.BirthDate,
            userProfileDto.BirthPlace, userProfileDto.FullName);
    }

    public async Task<Guid> DeleteAccountAsync(Guid currentUserId)
    {
        try
        {
            var awakeningAgent = _agentFactory.CreateGAgent<AwakeningGAgent>(currentUserId);
            await awakeningAgent.ResetTodayContentAsync();
        }catch(Exception e)
        {
            _logger.LogError(e,"IAwakeningGAgent ResetTodayContentAsync error currentUserId:"+currentUserId);
        }
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(currentUserId);
        return await manager.ClearAllAsync();
    }

    public async Task<CreateShareIdResponse> GenerateShareContentAsync(Guid currentUserId, CreateShareIdRequest request, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        try
        {
            var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(currentUserId);
            RequestContext.Set("GodGPTLanguage", language.ToString());
            var shareId = await manager.GenerateChatShareContentAsync(request.SessionId);
            return new CreateShareIdResponse
            {
                ShareId = GuidCompressor.CompressGuids(currentUserId, request.SessionId, shareId)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"GenerateShareContentAsync userId:{currentUserId}, sessionId:{request.SessionId}, error: {ex.Message} ");
            throw ex;
        }
    }

    public async Task<List<ChatMessage>> GetShareMessageListAsync(string shareString, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        if (shareString.IsNullOrWhiteSpace())
        {
            throw new UserFriendlyException("Invalid Share string");
        }

        Guid userId;
        Guid sessionId;
        Guid shareId;
        try
        {
            (userId, sessionId, shareId) = GuidCompressor.DecompressGuids(shareString);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Invalid Share string. {0}", shareString);
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidShare, language);
            throw new UserFriendlyException(localizedMessage);
        }

        try
        {
            var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
            RequestContext.Set("GodGPTLanguage", language.ToString());
            var shareLinkDto = await manager.GetChatShareContentAsync(sessionId, shareId);
            return shareLinkDto.Messages;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetShareMessageListAsync exception userId:{userId},shareId:{shareId}, error:{ex.Message}");
            throw ex;
        }
    }

    public async Task UpdateShowToastAsync(Guid currentUserId)
    {
        var userQuotaGAgent = _agentFactory.CreateGAgent<UserQuotaGAgent>(currentUserId);
        //No need to save immediately, can be executed in one step
        userQuotaGAgent.SetShownCreditsToastAsync(true);
    }

    public async Task<GrainResultDto<int>> UpdateUserCreditsAsync(Guid currentUserId, UpdateUserCreditsInput input)
    {
        var userQuotaGAgent =
            _agentFactory.CreateGAgent<UserQuotaGAgent>(input.UserId);
        return await userQuotaGAgent.UpdateCreditsAsync(currentUserId.ToString(), input.Credits);
    }

    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(Guid currentUserId, UpdateUserSubscriptionsInput input)
    {
        var userQuotaGAgent =
            _agentFactory.CreateGAgent<UserQuotaGAgent>(input.UserId);
        return await userQuotaGAgent.UpdateSubscriptionAsync(currentUserId.ToString(), input.PlanType, input.IsUltimate);
    }

    public async Task<GetInvitationInfoResponse> GetInvitationInfoAsync(Guid currentUserId)
    {
        var invitationAgent =  _agentFactory.CreateGAgent<InvitationGAgent>(currentUserId);
        var inviteCode = await invitationAgent.GenerateInviteCodeAsync();
        var invitationStatsDto = await invitationAgent.GetInvitationStatsAsync();
        var rewardTierDtos = await invitationAgent.GetRewardTiersAsync();
        return new GetInvitationInfoResponse
        {
            InviteCode = inviteCode,
            TotalInvites = invitationStatsDto.TotalInvites,
            ValidInvites = invitationStatsDto.ValidInvites,
            TotalCreditsEarned = invitationStatsDto.TotalCreditsEarned,
            RewardTiers = rewardTierDtos,
            TotalCreditsFromX = invitationStatsDto.TotalCreditsFromX,
            IsBound = invitationStatsDto.IsBound
        };
    }

    public async Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(Guid currentUserId,
        RedeemInviteCodeRequest input)
    {
        var codeType = InvitationCodeHelper.GetCodeType(input.InviteCode) ?? InvitationCodeType.FriendInvitation;
        if (codeType == InvitationCodeType.FriendInvitation)
        {
            var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(currentUserId);
            var result = await manager.RedeemInviteCodeAsync(input.InviteCode);
            return new RedeemInviteCodeResponse
            {
                IsValid = result,
                CodeType = codeType
            };
        } else if (codeType == InvitationCodeType.FreeTrialReward)
        {
            if (!input.IsWeb)
            {
                _logger.LogWarning("FreeTrialCode web only， {IsWeb}", input.IsWeb);
                return new RedeemInviteCodeResponse
                {
                    IsValid = false,
                    CodeType = InvitationCodeType.FreeTrialReward,
                    URL = null
                };
            }
            try
            {
                // Use PaymentService to create checkout session with trial code
                var result = await _paymentService.CreateSubscriptionAsync(
                    currentUserId,
                    NewPaymentPlatform.Stripe,
                    new SubscriptionRequest
                    {
                        CouponCode = input.InviteCode // TrialCode maps to CouponCode
                    });
                return new RedeemInviteCodeResponse
                {
                    IsValid = true,
                    CodeType = codeType,
                    URL = result.SessionUrl
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[GodGPTService][RedeemInviteCodeAsync] {UserId} invalid InviteCode {Code}", 
                    currentUserId.ToString(), input.InviteCode);
                return new RedeemInviteCodeResponse
                {
                    IsValid = false,
                    CodeType = codeType
                };
            }
        }
        else
        {
            _logger.LogError("[GodGPTService][RedeemInviteCodeAsync] {UserId} Unknown code type. {Code}", 
                currentUserId.ToString(), input.InviteCode);
            return new RedeemInviteCodeResponse
            {
                IsValid = false
            };
        }
    }

    #region Anonymous User Methods

    /// <summary>
    /// Create guest session for anonymous users (IP-based)
    /// </summary>
    public async Task<CreateGuestSessionResponseDto> CreateGuestSessionAsync(string clientIp, string? guider = null)
    {
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        
        // Check if user can still chat
        if (!await anonymousUserGrain.CanChatAsync())
        {
            var remainingChats = await anonymousUserGrain.GetRemainingChatsAsync();
            return new CreateGuestSessionResponseDto
            {
                RemainingChats = remainingChats,
                TotalAllowed = await GetMaxChatCountAsync()
            };
        }

        // Create new session (this will replace any existing session for the IP)
        await anonymousUserGrain.CreateGuestSessionAsync(guider);
        
        var remaining = await anonymousUserGrain.GetRemainingChatsAsync();
        return new CreateGuestSessionResponseDto
        {
            RemainingChats = remaining,
            TotalAllowed = await GetMaxChatCountAsync()
        };
    }

    /// <summary>
    /// Execute guest chat for anonymous users
    /// </summary>
    public async Task GuestChatAsync(string clientIp, string content, string chatId)
    {
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        await anonymousUserGrain.GuestChatAsync(content, chatId);
    }

    /// <summary>
    /// Get chat limits for anonymous users
    /// </summary>
    public async Task<GuestChatLimitsResponseDto> GetGuestChatLimitsAsync(string clientIp)
    { 
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        var remaining = await anonymousUserGrain.GetRemainingChatsAsync();
        
        return new GuestChatLimitsResponseDto
        {
            RemainingChats = remaining,
            TotalAllowed = await GetMaxChatCountAsync()
        };
    }

    /// <summary>
    /// Check if anonymous user can chat
    /// </summary>
    public async Task<bool> CanGuestChatAsync(string clientIp)
    {
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        return await anonymousUserGrain.CanChatAsync();
    }

    public async Task<UserProfileDto> SetVoiceLanguageAsync(Guid currentUserId, VoiceLanguageEnum voiceLanguage)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(currentUserId);
        await manager.SetVoiceLanguageAsync(voiceLanguage);
        return await manager.GetUserProfileAsync();
    }

    public async Task<ExecuteActionResultDto> CanUploadImageAsync(Guid currentUserId,GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        var userQuotaGAgent = _agentFactory.CreateGAgent<UserQuotaGAgent>(currentUserId);
        RequestContext.Set("GodGPTLanguage", language.ToString());
        return await userQuotaGAgent.CanUploadImageAsync();
    }


        public async Task<AwakeningContentDto?> GetTodayAwakeningAsync(Guid currentUserId, VoiceLanguageEnum language, string? region)
    {
        _logger.LogInformation("[GodGPTService][GetTodayAwakeningAsync] Starting for userId: {UserId}, language: {Language}, region: {Region}",
            currentUserId, language, region);
        
        try
        {
            var awakeningAgent = _agentFactory.CreateGAgent<AwakeningGAgent>(currentUserId);
            var result = await awakeningAgent.GetTodayAwakeningAsync(language, region);
            
            _logger.LogInformation("[GodGPTService][GetTodayAwakeningAsync] Completed for userId: {UserId}, result: {HasResult}",
                currentUserId, result != null);
            if (result == null)
            {
                return new AwakeningContentDto()
                {
                    AwakeningMessage = "",
                    AwakeningLevel = 0,
                    Status = (int)AwakeningStatus.NotStarted
                };
            }
            return new AwakeningContentDto()
            {
                AwakeningMessage = result.AwakeningMessage,
                AwakeningLevel = result.AwakeningLevel,
                Status = (int)result.Status
            };;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTService][GetTodayAwakeningAsync] Error for userId: {UserId}, language: {Language}, region: {Region}",
                currentUserId, language, region);
            throw;
        }
    }

    #endregion

    /// <summary>
    /// Get max chat count from AnonymousUserGAgent configuration, default to 3 if unable to retrieve
    /// </summary>
    private async Task<int> GetMaxChatCountAsync()
    {
        try
        {
            // Use a dummy IP to get configuration from AnonymousUserGAgent
            var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId("127.0.0.1"));
            var configGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
            return await configGrain.GetMaxChatCountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get max chat count from configuration, using default: 3");
            return 3;
        }
    }

    public async Task<QuantumShareResponseDto> GetShareKeyWordWithAIAsync(Guid sessionId, string? content, string? region, SessionType sessionType, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        _logger.LogDebug($"[GodGPTService][GetShareKeyWordWithAIAsync] http start: sessionId={sessionId}, sessionType={sessionType}");
        var responseContent = "";
        try
        {
            var godChat = _clusterClient.GetGrain<IGodChat>(sessionId);
            var chatId = Guid.NewGuid().ToString();
            var response = await godChat.ChatWithHistory(sessionId, string.Empty, content,
                chatId, null, true, region);
            responseContent = response.IsNullOrEmpty() ? sessionType.GetDefaultContent(language) : response.FirstOrDefault().Content;
            _logger.LogDebug(
                $"[GodGPTService][GetShareKeyWordWithAIAsync] completed for sessionId={sessionId}, responseContent:{responseContent}");
        }
        catch (Exception ex)
        {
            responseContent = sessionType.GetDefaultContent(language);
            _logger.LogError(ex, $"[GodGPTService][GetShareKeyWordWithAIAsync] error for sessionId={sessionId}, sessionType={sessionType}");
        }

        return new QuantumShareResponseDto()
        {
            Success = true,
            Content = responseContent,
        };
    }

    public async Task<PagedResultDto<RewardHistoryDto>> GetCreditsHistoryAsync(Guid currentUserId,
        GetCreditsHistoryInput input)
    {
        var invitationAgent =  _agentFactory.CreateGAgent<InvitationGAgent>(currentUserId);
        var rewardHistoryDtos = await invitationAgent.GetRewardHistoryAsync(new GetRewardHistoryRequestDto
        {
            PageNo = input.Page,
            PageSize = input.PageSize
        });
        return rewardHistoryDtos;
    }

    // NOTE: Twitter methods removed - feature deprecated
    
    public async Task<bool> ResetAwakeningStateForTestingAsync(Guid userId)
    {
        _logger.LogInformation("[GodGPTService][ResetAwakeningStateForTestingAsync] Starting for userId: {UserId}", userId);
        
        try
        {
            var awakeningAgent = _agentFactory.CreateGAgent<AwakeningGAgent>(userId);
            bool resetSuccess = await awakeningAgent.ResetAwakeningStateForTestingAsync();
            
            _logger.LogInformation("[GodGPTService][ResetAwakeningStateForTestingAsync] Completed for userId: {UserId}, success: {Success}",
                userId, resetSuccess);
            
            return resetSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTService][ResetAwakeningStateForTestingAsync] Error resetting awakening state for userId: {UserId}", userId);
            return false;
        }
    }

    public async Task<AppRatingRecordDto> RecordAppRatingAsync(Guid currentUserId, RecordAppRatingInput input)
    {
        var grainId = CommonHelper.StringToGuid(input.DeviceId);
        var userStatisticsGAgent = _agentFactory.CreateGAgent<UserStatisticsGAgent>(grainId);
        return await userStatisticsGAgent.RecordAppRatingAsync(currentUserId, input.Platform, input.DeviceId);
    }

    public async Task<bool> CanUserRateAppAsync(Guid currentUserId, CanUserRateAppInput input)
    {
        var grainId = CommonHelper.StringToGuid(input.DeviceId);
        var userStatisticsGAgent = _agentFactory.CreateGAgent<UserStatisticsGAgent>(grainId);
        return await userStatisticsGAgent.CanUserRateAppAsync(input.DeviceId);
    }

    public Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(Guid currentUserId, GetInvitationCodeTypeRequest input)
    {
        var codeType = InvitationCodeHelper.GetCodeType(input.InviteCode);
        return Task.FromResult(new GetInvitationCodeTypeResponse
        {
            CodeType = codeType ?? InvitationCodeType.FriendInvitation
        });
    }

    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid currentUserId,
        GenerateFreeTrialCodeRequest input)
    {
        var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var factoryGAgent = _agentFactory.CreateGAgent<FreeTrialCodeFactoryGAgent>(CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId));
        return await factoryGAgent.GenerateCodesAsync(new GenerateCodesRequestDto
        {
            BatchId = batchId,
            ProductId = input.ProductId,
            Platform = input.Platform,
            TrialDays = input.TrialDays,
            StartTime = input.StartTime,
            EndTime = input.EndTime,
            Quantity = input.Quantity,
            OperatorUserId = currentUserId
        });
    }

    public async Task<BatchInfoDto> GetBatchInfoAsync(string batchId)
    {
        var factoryGAgent = _agentFactory.CreateGAgent<FreeTrialCodeFactoryGAgent>(CommonHelper.GetFreeTrialCodeFactoryGAgentId(long.Parse(batchId)));
        return await factoryGAgent.GetBatchInfoAsync();
    }
    
    public async Task<bool> CheckIsManager(string userId)
    {
        if (userId.IsNullOrEmpty())
        {
            return false;
        }

        return _managerOptions.CurrentValue.ManagerIds.Contains(userId);
    }

    public async Task<bool> CheckIsManager(Guid? currentUserId)
    {
        if (currentUserId == Guid.Empty || currentUserId == null)
        {
            return false;
        }
        
        return _managerOptions.CurrentValue.ManagerIds.Contains(currentUserId.ToString());
    }

    private bool TryGetUserIdFromMetadata(IDictionary<string, string> metadata, out string userId)
    {
        userId = null;
        if (metadata != null && metadata.TryGetValue("internal_user_id", out var id) && !string.IsNullOrEmpty(id))
        {
            userId = id;
            return true;
        }
        return false;
    }
}

public static class GuidCompressor
{
    public static string CompressGuids(Guid guid1, Guid guid2, Guid guid3)
    {
        var combinedBytes = CombineBytes(guid1.ToByteArray(), guid2.ToByteArray());
        combinedBytes = CombineBytes(combinedBytes, guid3.ToByteArray());

        var base64 = Convert.ToBase64String(combinedBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "");
        return base64;
    }

    public static (Guid, Guid, Guid) DecompressGuids(string compressedString)
    {
        var restoredBase64 = compressedString
            .Replace('-', '+')
            .Replace('_', '/')
            .PadRight(compressedString.Length + (4 - compressedString.Length % 4) % 4, '=');

        var bytes = Convert.FromBase64String(restoredBase64);

        var guid1Bytes = new byte[16];
        var guid2Bytes = new byte[16];
        var guid3Bytes = new byte[16];
        Array.Copy(bytes, 0, guid1Bytes, 0, 16);
        Array.Copy(bytes, 16, guid2Bytes, 0, 16);
        Array.Copy(bytes, 32, guid3Bytes, 0, 16);

        return (new Guid(guid1Bytes), new Guid(guid2Bytes), new Guid(guid3Bytes));
    }

    private static byte[] CombineBytes(byte[] bytes1, byte[] bytes2)
    {
        var combined = new byte[bytes1.Length + bytes2.Length];
        Buffer.BlockCopy(bytes1, 0, combined, 0, bytes1.Length);
        Buffer.BlockCopy(bytes2, 0, combined, bytes1.Length, bytes2.Length);
        return combined;
    }
}