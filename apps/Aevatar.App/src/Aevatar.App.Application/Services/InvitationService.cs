using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.Invitation;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.App.Services.Subscription;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserInvitation;
using Aevatar.GodGPT.Dtos;
using Aevatar.Dtos;
using CsPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using CsPaymentPlatform = Aevatar.Application.Grains.Common.Constants.PaymentPlatform;
using Aevatar.Payment.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewPaymentPlatform = Aevatar.Payment.Abstractions.PaymentPlatform;
using InvitationProtos = Aevatar.Agents.GodGPT.Protos.Invitation;
using CsInvitationCodeType = Aevatar.Application.Grains.Common.Constants.InvitationCodeType;
using PaymentStripeOptions = Aevatar.Payment.Providers.StripeOptions;
using PaymentStripeProductConfig = Aevatar.Payment.Providers.StripeProductConfig;

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
    private readonly ISubscriptionProductService _subscriptionProductService;
    private readonly IOptionsMonitor<CreditsOptions> _creditsOptions;
    private readonly IOptionsMonitor<PaymentStripeOptions> _stripeOptions;

    public InvitationService(
        IGAgentActorFactory actorFactory,
        ILogger<InvitationService> logger,
        IPaymentService paymentService,
        ISubscriptionProductService subscriptionProductService,
        IOptionsMonitor<CreditsOptions> creditsOptions,
        IOptionsMonitor<PaymentStripeOptions> stripeOptions)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _paymentService = paymentService;
        _creditsOptions = creditsOptions;
        _stripeOptions = stripeOptions;
        _subscriptionProductService = subscriptionProductService;
    }

    private async Task<IInvitationGAgent> GetInvitationAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(userId.ToString());
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
        _logger.LogInformation("[InvitationService] Redeeming invite code for user {UserId}, code {Code}", 
            userId, input.InviteCode);

        var protoCodeType = InvitationCodeHelper.GetCodeType(input.InviteCode);
        var codeType = protoCodeType.HasValue 
            ? (CsInvitationCodeType)(int)protoCodeType.Value 
            : CsInvitationCodeType.FriendInvitation;
        
        _logger.LogInformation("[InvitationService] Invite code type resolved. UserId: {UserId}, Code: {Code}, CodeType: {CodeType}",
            userId, input.InviteCode, codeType);

        if (codeType == CsInvitationCodeType.FriendInvitation)
        {
            // Use UserInvitationGAgent for friend invitation redemption
            var userInvitationActor = await _actorFactory.CreateGAgentActorAsync<UserInvitationGAgent>(userId.ToString());
            var userInvitationGAgent = userInvitationActor.As<IUserInvitationGAgent>();
            var result = await userInvitationGAgent.RedeemInviteCodeAsync(input.InviteCode);
            _logger.LogInformation("[InvitationService] Friend invite redemption result. UserId: {UserId}, Code: {Code}, Success: {Result}",
                userId, input.InviteCode, result);
            
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
                var batchId = InvitationCodeHelper.ParseBatchTimestampFromCode(input.InviteCode);
                var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId);
                var actor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(agentId.ToString());
                var factoryAgent = actor.As<IFreeTrialCodeFactoryGAgent>();
                var isAvailable = await factoryAgent.ValidateCodeAvailableAsync(new ValidateCodeRequestProto
                {
                    Code = input.InviteCode
                });
                if (!isAvailable)
                {
                    _logger.LogWarning("[InvitationService] FreeTrialCode not available. UserId: {UserId}, Code: {Code}", 
                        userId, input.InviteCode);
                    return new RedeemInviteCodeResponse
                    {
                        IsValid = false,
                        CodeType = codeType,
                        URL = null
                    };
                }
                
                var batchInfo = await factoryAgent.GetBatchInfoAsync();
                if (batchInfo.Config == null)
                {
                    _logger.LogWarning("[InvitationService] FreeTrialCode batch config missing. UserId: {UserId}, Code: {Code}", 
                        userId, input.InviteCode);
                    return new RedeemInviteCodeResponse
                    {
                        IsValid = false,
                        CodeType = codeType,
                        URL = null
                    };
                }
                
                if (!TryMapPaymentPlatform(batchInfo.Config.Platform, out var paymentPlatform))
                {
                    _logger.LogWarning("[InvitationService] Unsupported payment platform for FreeTrialCode. UserId: {UserId}, Code: {Code}, Platform: {Platform}", 
                        userId, input.InviteCode, batchInfo.Config.Platform);
                    return new RedeemInviteCodeResponse
                    {
                        IsValid = false,
                        CodeType = codeType,
                        URL = null
                    };
                }
                
                var result = await _paymentService.CreateSubscriptionAsync(
                    userId,
                    paymentPlatform,
                    new SubscriptionRequest
                    {
                        ProductId = batchInfo.Config.ProductId,
                        TrialDays = batchInfo.Config.TrialDays
                    });
                
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
                
                var marked = await factoryAgent.MarkCodeAsUsedAsync(new MarkCodeUsedRequestProto
                {
                    Code = input.InviteCode,
                    UserId = userId.ToString()
                });
                if (!marked)
                {
                    _logger.LogWarning("[InvitationService] FreeTrialCode marked used failed. UserId: {UserId}, Code: {Code}", 
                        userId, input.InviteCode);
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

    private static bool TryMapPaymentPlatform(FactoryPaymentPlatform platform, out NewPaymentPlatform mappedPlatform)
    {
        switch (platform)
        {
            case FactoryPaymentPlatform.Stripe:
                mappedPlatform = NewPaymentPlatform.Stripe;
                return true;
            case FactoryPaymentPlatform.AppStore:
                mappedPlatform = NewPaymentPlatform.AppStore;
                return true;
            case FactoryPaymentPlatform.GooglePlay:
                mappedPlatform = NewPaymentPlatform.GooglePlay;
                return true;
            default:
                mappedPlatform = NewPaymentPlatform.Stripe;
                return false;
        }
    }

    private bool IsOperatorAuthorized(Guid userId)
    {
        var operators = _creditsOptions.CurrentValue.OperatorUserId ?? new List<string>();
        return operators.Contains(userId.ToString());
    }

    private async Task<(PaymentStripeProductConfig?, string)> GetStripeProductConfigAsync(string productId)
    {
        var errorMessage = string.Empty;

        var product = await _subscriptionProductService.GetProductByPlatformPriceIdAsync(productId);
        if (product != null)
        {
            return (new PaymentStripeProductConfig
            {
                PriceId = product.PlatformPriceId,
                PlanType = (int)product.PlanType,
                Amount = (decimal)product.Price,
                Currency = product.Currency,
                IsUltimate = product.IsUltimate,
                Mode = "subscription",
                Description = product.Description,
                Name = product.Name
            }, errorMessage);
        }

        var products = _stripeOptions.CurrentValue.Products;
        if (products == null || products.Count == 0)
        {
            errorMessage = "Stripe products are not configured";
            return (null, errorMessage);
        }

        var matched = products.FirstOrDefault(p => p.PriceId == productId);
        if (matched == null)
        {
            errorMessage = $"Invalid priceId: {productId}. Product not found in configuration.";
            return (null, errorMessage);
        }

        return (matched,errorMessage);
    }

    private static FactoryPlanType MapPlanType(int planType)
    {
        return planType switch
        {
            1 => FactoryPlanType.Day,
            2 => FactoryPlanType.Month,
            3 => FactoryPlanType.Year,
            4 => FactoryPlanType.Week,
            _ => FactoryPlanType.None
        };
    }

    private static GenerateCodesResultDto BuildGenerateCodesFailure(string message)
    {
        return new GenerateCodesResultDto
        {
            Success = false,
            Message = message,
            Codes = new HashSet<string>(),
            GeneratedCount = 0,
            ErrorCode = FreeTrialCodeError.InternalError,
            BatchId = 0
        };
    }

    public Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(Guid userId, GetInvitationCodeTypeRequest request)
    {
        var protoCodeType = InvitationCodeHelper.GetCodeType(request.InviteCode);
        var codeType = protoCodeType.HasValue 
            ? (CsInvitationCodeType)(int)protoCodeType.Value 
            : CsInvitationCodeType.FriendInvitation;
        return Task.FromResult(new GetInvitationCodeTypeResponse
        {
            CodeType = codeType
        });
    }

    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid userId, GenerateFreeTrialCodeRequest request)
    {
        _logger.LogInformation("[InvitationService] Generating free trial code for user {UserId}", userId);

        if (!IsOperatorAuthorized(userId))
        {
            _logger.LogWarning("[InvitationService] Unauthorized attempt to generate codes by user {UserId}", userId);
            return BuildGenerateCodesFailure("Unauthorized attempt to generate code");
        }

        if (request.Platform != CsPaymentPlatform.Stripe)
        {
            _logger.LogWarning("[InvitationService] Unsupported payment platform: {Platform}", request.Platform);
            return BuildGenerateCodesFailure($"Unsupported payment platform: {request.Platform}");
        }

        var (product, productError) = await GetStripeProductConfigAsync(request.ProductId);
        if (product == null)
        {
            _logger.LogWarning("[InvitationService] Invalid product id for free trial code: {ProductId}", request.ProductId);
            return BuildGenerateCodesFailure(productError);
        }

        var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId);
        
        var actor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(agentId.ToString());
        var agent = actor.As<IFreeTrialCodeFactoryGAgent>();

        var batchConfig = new BatchConfig
        {
            TrialDays = request.TrialDays,
            ProductId = product.PriceId,
            PlanType = MapPlanType((int)product.PlanType),
            IsUltimate = product.IsUltimate,
            Platform = (FactoryPaymentPlatform)request.Platform,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.StartTime, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.EndTime, DateTimeKind.Utc)),
            Description = request.Description ?? string.Empty
        };
        
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
            Description = request.Description ?? string.Empty,
            BatchConfig = batchConfig
        };
        
        var protoResult = await agent.GenerateCodesAsync(protoRequest);
        
        // Convert Protobuf to DTO
        return new GenerateCodesResultDto
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            Codes = protoResult.Codes.ToHashSet(),
            GeneratedCount = protoResult.GeneratedCount,
            ErrorCode = (FreeTrialCodeError)protoResult.ErrorCode,
            BatchId = batchId
        };
    }

    public async Task<BatchInfoDto> GetBatchInfoAsync(string batchId)
    {
        _logger.LogInformation("[InvitationService] Getting batch info for batch {BatchId}", batchId);
        
        var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(long.Parse(batchId));
        var actor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(agentId.ToString());
        var agent = actor.As<IFreeTrialCodeFactoryGAgent>();
        
        var protoResult = await agent.GetBatchInfoAsync();
        
        // Convert Protobuf to DTO
        var batchInfo = new BatchInfoDto
        {
            BatchId = protoResult.BatchId,
            TotalGenerated = protoResult.TotalGenerated,
            UsedCount = protoResult.UsedCount,
            CreationTime = protoResult.CreationTime.ToDateTime().ToUniversalTime(),
            LastGenerationTime = protoResult.LastGenerationTime?.ToDateTime().ToUniversalTime() ?? DateTime.MinValue.ToUniversalTime(),
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
            PlanType = (QuotaPlanType)(int)config.PlanType,
            IsUltimate = config.IsUltimate,
            Platform = (CsPaymentPlatform)config.Platform,
            StartTime = config.StartTime?.ToDateTime().ToUniversalTime() ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
            EndTime = config.EndTime?.ToDateTime().ToUniversalTime() ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
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

