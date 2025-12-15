using System;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.Invitation;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.Application.Grains.Invitation;
using Aevatar.GodGPT.Dtos;
using Aevatar.Dtos;
using CsPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using CsPaymentPlatform = Aevatar.Application.Grains.Common.Constants.PaymentPlatform;
using Aevatar.Payment.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using NewPaymentPlatform = Aevatar.Payment.Abstractions.PaymentPlatform;
using InvitationProtos = Aevatar.Agents.GodGPT.Protos.Invitation;
using CsInvitationCodeType = Aevatar.Application.Grains.Common.Constants.InvitationCodeType;

namespace Aevatar.App.Application.Services;

/// <summary>
/// Invitation service implementation - manages invitation codes and rewards
/// Uses InvitationGAgent and InviteCodeGAgent architecture
/// </summary>
public class InvitationService : IInvitationService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<InvitationService> _logger;
    private readonly IPaymentService _paymentService;

    public InvitationService(
        IGAgentActorFactory actorFactory,
        ILogger<InvitationService> logger,
        IPaymentService paymentService)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _paymentService = paymentService;
    }

    private async Task<IInvitationGAgent> GetInvitationAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(userId);
        return actor.As<IInvitationGAgent>();
    }

    public async Task<GetInvitationInfoResponse> GetInvitationInfoAsync(Guid userId)
    {
        _logger.LogInformation("[InvitationService] Getting invitation info for user {UserId}", userId);

        var invitationAgent = await GetInvitationAgentAsync(userId);
        var inviteCode = await invitationAgent.GenerateInviteCodeAsync();
        var invitationStatsProto = await invitationAgent.GetInvitationStatsAsync();
        var rewardTierResponse = await invitationAgent.GetRewardTiersAsync();
        
        return new GetInvitationInfoResponse
        {
            InviteCode = inviteCode,
            TotalInvites = invitationStatsProto.TotalInvites,
            ValidInvites = invitationStatsProto.ValidInvites,
            TotalCreditsEarned = invitationStatsProto.TotalCreditsEarned,
            RewardTiers = rewardTierResponse.Tiers.Select(FromProto).ToList(),
            TotalCreditsFromX = invitationStatsProto.TotalCreditsFromX,
            IsBound = invitationStatsProto.IsBound
        };
    }

    public async Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(Guid userId, RedeemInviteCodeRequest input)
    {
        _logger.LogInformation("[InvitationService] Redeeming invite code for user {UserId}", userId);

        var codeType = InvitationCodeHelper.GetCodeType(input.InviteCode) ?? CsInvitationCodeType.FriendInvitation;
        
        if (codeType == CsInvitationCodeType.FriendInvitation)
        {
            // Use ChatManager for friend invitation redemption
            var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userId);
            var manager = managerActor.As<IChatManagerGAgent>();
            var result = await manager.RedeemInviteCodeAsync(input.InviteCode);
            
            return new RedeemInviteCodeResponse
            {
                IsValid = result,
                CodeType = codeType
            };
        }
        else if (codeType == CsInvitationCodeType.FreeTrialReward)
        {
            if (!input.IsWeb)
            {
                _logger.LogWarning("[InvitationService] FreeTrialCode web only, {IsWeb}", input.IsWeb);
                return new RedeemInviteCodeResponse
                {
                    IsValid = false,
                    CodeType = CsInvitationCodeType.FreeTrialReward,
                    URL = null
                };
            }
            
            try
            {
                // Use PaymentService to create checkout session with trial code
                // Stripe will validate the coupon code - if invalid, it will throw exception or return Success=false
                var result = await _paymentService.CreateSubscriptionAsync(
                    userId,
                    NewPaymentPlatform.Stripe,
                    new SubscriptionRequest
                    {
                        CouponCode = input.InviteCode // TrialCode maps to CouponCode
                    });
                
                // Check if Stripe successfully created the checkout session
                if (!result.Success || string.IsNullOrEmpty(result.SessionUrl))
                {
                    _logger.LogWarning("[InvitationService] Failed to create checkout session for user {UserId}, code {Code}. Error: {Error}", 
                        userId, input.InviteCode, result.ErrorMessage);
                    return new RedeemInviteCodeResponse
                    {
                        IsValid = false,
                        CodeType = codeType,
                        URL = null
                    };
                }
                
                return new RedeemInviteCodeResponse
                {
                    IsValid = true,
                    CodeType = codeType,
                    URL = result.SessionUrl
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[InvitationService] {UserId} invalid InviteCode {Code}", 
                    userId.ToString(), input.InviteCode);
                return new RedeemInviteCodeResponse
                {
                    IsValid = false,
                    CodeType = codeType
                };
            }
        }
        else
        {
            _logger.LogError("[InvitationService] {UserId} Unknown code type. {Code}", 
                userId.ToString(), input.InviteCode);
            return new RedeemInviteCodeResponse
            {
                IsValid = false
            };
        }
    }

    public Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(Guid userId, GetInvitationCodeTypeRequest request)
    {
        var codeType = InvitationCodeHelper.GetCodeType(request.InviteCode);
        return Task.FromResult(new GetInvitationCodeTypeResponse
        {
            CodeType = codeType ?? CsInvitationCodeType.FriendInvitation
        });
    }

    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid userId, GenerateFreeTrialCodeRequest request)
    {
        _logger.LogInformation("[InvitationService] Generating free trial code for user {UserId}", userId);

        var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId);
        
        var actor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(agentId);
        var agent = actor.As<IFreeTrialCodeFactoryGAgent>();
        
        // Convert DTO to Protobuf
        var protoRequest = new GenerateCodesRequestProto
        {
            BatchId = batchId,
            ProductId = request.ProductId,
            Platform = (FactoryPaymentPlatform)request.Platform,
            TrialDays = request.TrialDays,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.StartTime, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.EndTime, DateTimeKind.Utc)),
            Quantity = request.Quantity,
            OperatorUserId = userId.ToString(),
            Description = request.Description ?? string.Empty
        };
        
        var protoResult = await agent.GenerateCodesAsync(protoRequest);
        
        // Convert Protobuf to DTO
        return new GenerateCodesResultDto
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            Codes = protoResult.Codes.ToHashSet(),
            GeneratedCount = protoResult.GeneratedCount,
            ErrorCode = (FreeTrialCodeError)protoResult.ErrorCode
        };
    }

    public async Task<BatchInfoDto> GetBatchInfoAsync(string batchId)
    {
        _logger.LogInformation("[InvitationService] Getting batch info for batch {BatchId}", batchId);
        
        var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(long.Parse(batchId));
        var actor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(agentId);
        var agent = actor.As<IFreeTrialCodeFactoryGAgent>();
        
        var protoResult = await agent.GetBatchInfoAsync();
        
        // Convert Protobuf to DTO
        var batchInfo = new BatchInfoDto
        {
            BatchId = protoResult.BatchId,
            TotalGenerated = protoResult.TotalGenerated,
            UsedCount = protoResult.UsedCount,
            CreationTime = protoResult.CreationTime.ToDateTime(),
            LastGenerationTime = protoResult.LastGenerationTime.ToDateTime(),
            Status = (FreeTrialCodeFactoryStatus)protoResult.Status,
            GeneratedCodes = protoResult.GeneratedCodes.ToList(),
            UsedCodes = protoResult.UsedCodes.ToList()
        };
        
        if (protoResult.Config != null)
        {
            batchInfo.Config = ConvertBatchConfigFromProto(protoResult.Config);
        }
        
        return batchInfo;
    }
    
    private FreeTrialCodeBatchConfig ConvertBatchConfigFromProto(BatchConfig config)
    {
        return new FreeTrialCodeBatchConfig
        {
            TrialDays = config.TrialDays,
            ProductId = config.ProductId,
            PlanType = (CsPlanType)config.PlanType,
            IsUltimate = config.IsUltimate,
            Platform = (CsPaymentPlatform)config.Platform,
            StartTime = config.StartTime?.ToDateTime() ?? DateTime.MinValue,
            EndTime = config.EndTime?.ToDateTime() ?? DateTime.MaxValue,
            Description = config.Description
        };
    }

    public async Task<PagedResultDto<RewardHistoryDto>> GetCreditsHistoryAsync(Guid userId, GetCreditsHistoryInput input)
    {
        _logger.LogInformation("[InvitationService] Getting credits history for user {UserId}", userId);

        var invitationAgent = await GetInvitationAgentAsync(userId);
        var request = new GetRewardHistoryRequestProto
        {
            PageNo = input.Page,
            PageSize = input.PageSize
        };
        
        // Note: GetCreditsHistoryInput doesn't have RewardType, so we skip filtering
        // If needed, add RewardType to GetCreditsHistoryInput DTO
        
        var response = await invitationAgent.GetRewardHistoryAsync(request);
        
        return new PagedResultDto<RewardHistoryDto>(
            response.Items.Select(FromProto).ToList(),
            response.TotalCount,
            response.PageNo,
            response.PageSize
        );
    }
    
    // ========== Proto Conversions ==========
    
    private static RewardTierDto FromProto(RewardTierProto proto)
    {
        return new RewardTierDto
        {
            InviteCount = proto.InviteCount,
            Credits = proto.Credits,
            IsCompleted = proto.IsCompleted
        };
    }
    
    private static RewardHistoryDto FromProto(RewardHistoryProto proto)
    {
        return new RewardHistoryDto
        {
            InviteeId = proto.InviteeId,
            Credits = proto.Credits,
            RewardType = proto.RewardType,
            IssuedAt = proto.IssuedAt?.ToDateTime() ?? DateTime.MinValue,
            IsScheduled = proto.IsScheduled,
            ScheduledDate = proto.ScheduledDate?.ToDateTime(),
            InvoiceId = proto.InvoiceId,
            TweetId = proto.TweetId
        };
    }
}

