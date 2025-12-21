using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Using QuotaPlanType from user_quota.proto as the unified plan type

namespace Aevatar.Application.Grains.FreeTrialCode;

/// <summary>
/// Interface for Free Trial Code Factory GAgent - manages batch-based free trial code generation
/// Each instance manages one batch of codes identified by BatchId
/// </summary>
public interface IFreeTrialCodeFactoryGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    Task<GenerateCodesResultDto> GenerateCodesAsync(GenerateCodesRequestDto request);
    Task<BatchInfoDto> GetBatchInfoAsync();
    Task<bool> MarkCodeAsUsedAsync(string code, string userId);
    Task<bool> ValidateCodeOwnershipAsync(string code);
    Task<bool> ValidateCodeAvailableAsync(string code);
}

[GAgent(nameof(FreeTrialCodeFactoryGAgent))]
public class FreeTrialCodeFactoryGAgent : GAgentBase<FreeTrialCodeFactoryState>, IFreeTrialCodeFactoryGAgent
{
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly IOptionsMonitor<CreditsOptions> _creditsOptions;

    private const int MaxQuantity = 10000;

    public FreeTrialCodeFactoryGAgent(
        Guid id,
        IOptionsMonitor<StripeOptions> stripeOptions,
        IOptionsMonitor<CreditsOptions> creditsOptions) : base(id)
    {
        _stripeOptions = stripeOptions;
        _creditsOptions = creditsOptions;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Free Trial Code Factory Management GAgent");
    }

    public async Task<GenerateCodesResultDto> GenerateCodesAsync(GenerateCodesRequestDto request)
    {
        if (!IsUserAuthorizedToGenerateCode(request.OperatorUserId.ToString()))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Unauthorized attempt to generate codes by user {OperatorUserId}",
                request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = "Unauthorized attempt to generate code",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }
        
        await InitializeFactoryAsync(request);

        if (!State.HasBatchId)
        {
            Logger.LogError(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Factory not initialized. OperatorUserId: {OperatorUserId}",
                request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = "Factory not initialized",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        if (State.Status != FactoryStatus.Active)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Factory is not active. BatchId: {BatchId}, Status: {Status}, OperatorUserId: {OperatorUserId}",
                State.BatchId, State.Status, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = $"Factory is not active. Status: {State.Status}",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        if (request.Quantity > MaxQuantity)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Requested quantity exceeds maximum. BatchId: {BatchId}, Requested: {RequestedQuantity}, Max: {MaxQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, request.Quantity, MaxQuantity, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = $"Quantity cannot exceed batch max quantity: {MaxQuantity}",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        if (State.TotalCodesGenerated + request.Quantity > MaxQuantity)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Would exceed batch capacity. BatchId: {BatchId}, Current: {CurrentGenerated}, Requested: {RequestedQuantity}, Max: {MaxQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, State.TotalCodesGenerated, request.Quantity, MaxQuantity, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message =
                    $"Would exceed batch capacity. Current: {State.TotalCodesGenerated}, Requested: {request.Quantity}, Max: {MaxQuantity}",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        try
        {
            var codes = await GenerateCodesInternalAsync(request.Quantity);

            if (codes.Count != request.Quantity)
            {
                Logger.LogWarning(
                    "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Generated code count mismatch. BatchId: {BatchId}, Expected: {ExpectedQuantity}, Actual: {ActualCount}, OperatorUserId: {OperatorUserId}",
                    State.BatchId, request.Quantity, codes.Count, request.OperatorUserId);
            }

            var evt = new GenerateCodesEvent
            {
                Quantity = request.Quantity,
                Status = FactoryStatus.Completed,
                CreationTime = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            evt.GeneratedCodes.AddRange(codes);
            
            RaiseEvent(evt);
            await ConfirmEventsAsync();

            Logger.LogInformation(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Successfully generated codes. BatchId: {BatchId}, Count: {Count}, OperatorUserId: {OperatorUserId}",
                State.BatchId, codes.Count, request.OperatorUserId);

            return new GenerateCodesResultDto
            {
                Success = true,
                Message = "Codes generated successfully",
                Codes = codes,
                GeneratedCount = codes.Count,
                ErrorCode = FreeTrialCodeError.None
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Error generating codes. BatchId: {BatchId}, RequestedQuantity: {RequestedQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, request.Quantity, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = "Internal error occurred while generating codes",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }
    }

    public async Task<bool> InitializeFactoryAsync(GenerateCodesRequestDto request)
    {
        if (State.HasBatchId)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][InitializeFactoryAsync] Factory already initialized. BatchId: {BatchId}, OperatorUserId: {OperatorUserId}", 
                State.BatchId, request.OperatorUserId);
            return false;
        }

        var stripeProduct = await GetStripeProductConfigAsync(request.ProductId);

        var batchConfig = new BatchConfig
        {
            TrialDays = request.TrialDays,
            ProductId = stripeProduct.PriceId,
            PlanType = (FactoryPlanType)stripeProduct.PlanType,
            IsUltimate = stripeProduct.IsUltimate,
            Platform = FactoryPaymentPlatform.Stripe,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.StartTime, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.EndTime, DateTimeKind.Utc)),
            Description = request.Description ?? string.Empty
        };

        RaiseEvent(new InitializeFactoryEvent
        {
            BatchId = request.BatchId,
            OperatorUserId = request.OperatorUserId.ToString(),
            BatchConfig = batchConfig,
            CreationTime = Timestamp.FromDateTime(DateTime.UtcNow),
            Status = FactoryStatus.Active
        });

        await ConfirmEventsAsync();

        Logger.LogInformation(
            "[FreeTrialCodeFactoryGAgent][InitializeFactoryAsync] Factory initialized successfully. BatchId: {BatchId}, OperatorUserId: {OperatorUserId}",
            request.BatchId, request.OperatorUserId);

        return true;
    }

    public Task<BatchInfoDto> GetBatchInfoAsync()
    {
        var batchInfo = new BatchInfoDto
        {
            BatchId = State.HasBatchId ? State.BatchId : 0,
            TotalGenerated = State.TotalCodesGenerated,
            UsedCount = State.UsedCount,
            CreationTime = State.CreationTime?.ToDateTime() ?? DateTime.MinValue,
            LastGenerationTime = State.LastGenerationTime?.ToDateTime() ?? DateTime.MinValue,
            Config = ConvertBatchConfigToDto(State.BatchConfig),
            Status = (FreeTrialCodeFactoryStatus)State.Status,
            GeneratedCodes = State.GeneratedCodes.ToList(),
            UsedCodes = State.UsedCodes.ToList()
        };

        return Task.FromResult(batchInfo);
    }

    public async Task<bool> MarkCodeAsUsedAsync(string code, string userId)
    {
        if (!await ValidateCodeOwnershipAsync(code))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code not found in batch. Code: {Code}, BatchId: {BatchId}, UserId: {UserId}", 
                code, State.BatchId, userId);
            return false;
        }

        if (State.UsedCodes.Contains(code))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code already used. Code: {Code}, BatchId: {BatchId}, UserId: {UserId}", 
                code, State.BatchId, userId);
            return false;
        }

        RaiseEvent(new MarkCodeUsedEvent
        {
            Code = code,
            UserId = userId,
            UsedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();

        Logger.LogInformation(
            "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code marked as used successfully. Code: {Code}, UserId: {UserId}, BatchId: {BatchId}",
            code, userId, State.BatchId);

        return true;
    }

    public Task<bool> ValidateCodeOwnershipAsync(string code)
    {
        if (!State.HasBatchId)
        {
            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(code)
            || State.GeneratedCodes.Count == 0
            || !InvitationCodeHelper.IsValidFreeTrialCodeFormat(code))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (!InvitationCodeHelper.IsCodeFromBatch(code, State.BatchId))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(State.GeneratedCodes.Contains(code));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, 
                "[FreeTrialCodeFactoryGAgent][ValidateCodeOwnershipAsync] Error validating code ownership. Code: {Code}, BatchId: {BatchId}", 
                code, State.BatchId);
            return Task.FromResult(false);
        }
    }

    public async Task<bool> ValidateCodeAvailableAsync(string code)
    {
        if (!await ValidateCodeOwnershipAsync(code))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code not found in batch. Code: {Code}, BatchId: {BatchId}", 
                code, State.BatchId);
            return false;
        }

        if (State.UsedCodes.Contains(code))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code already used. Code: {Code}, BatchId: {BatchId}", 
                code, State.BatchId);
            return false;
        }

        if (State.BatchConfig == null)
        {
            Logger.LogWarning("[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] BatchConfig is null. Code: {Code}, BatchId: {BatchId}", 
                code, State.BatchId);
            return false;
        }

        var currentTime = DateTime.UtcNow;
        var startTime = State.BatchConfig.StartTime?.ToDateTime() ?? DateTime.MinValue;
        var endTime = State.BatchConfig.EndTime?.ToDateTime() ?? DateTime.MaxValue;

        if (currentTime < startTime)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code not yet valid. Code: {Code}, BatchId: {BatchId}, Current: {CurrentTime}, Start: {StartTime}",
                code, State.BatchId, currentTime, startTime);
            return false;
        }

        if (currentTime > endTime)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code has expired. Code: {Code}, BatchId: {BatchId}, Current: {CurrentTime}, End: {EndTime}",
                code, State.BatchId, currentTime, endTime);
            return false;
        }

        Logger.LogDebug(
            "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code is available for use. Code: {Code}, BatchId: {BatchId}", 
            code, State.BatchId);
        return true;
    }

    private Task<HashSet<string>> GenerateCodesInternalAsync(int quantity)
    {
        var codes = new HashSet<string>();
        var usedCodes = new HashSet<string>(State.GeneratedCodes);

        var codeType = InviteCodeType.FreeTrialReward;
        var unixTimestamp = State.HasBatchId ? State.BatchId : 0;

        for (int i = 0; i < quantity; i++)
        {
            string fullCode;
            do
            {
                fullCode = InvitationCodeHelper.GenerateOptimizedCode(codeType, unixTimestamp);
            } while (usedCodes.Contains(fullCode) || codes.Contains(fullCode));

            codes.Add(fullCode);
            usedCodes.Add(fullCode);
        }

        return Task.FromResult(codes);
    }

    private bool IsUserAuthorizedToGenerateCode(string operatorUserId)
    {
        var authorizedUsers = _creditsOptions.CurrentValue.OperatorUserId;
        return authorizedUsers.Contains(operatorUserId);
    }

    private Task<StripeProduct> GetStripeProductConfigAsync(string priceId)
    {
        var productConfig = _stripeOptions.CurrentValue.Products.FirstOrDefault(p => p.PriceId == priceId);
        if (productConfig == null)
        {
            Logger.LogError(
                "[FreeTrialCodeFactoryGAgent][GetStripeProductConfigAsync] Invalid priceId: {PriceId}. Product not found in configuration.",
                priceId);
            throw new ArgumentException($"Invalid priceId: {priceId}. Product not found in configuration.");
        }

        Logger.LogDebug(
            "[FreeTrialCodeFactoryGAgent][GetStripeProductConfigAsync] Found product with priceId: {PriceId}, planType: {PlanType}, amount: {Amount} {Currency}",
            productConfig.PriceId, productConfig.PlanType, productConfig.Amount, productConfig.Currency);

        return Task.FromResult(productConfig);
    }

    private FreeTrialCodeBatchConfig? ConvertBatchConfigToDto(BatchConfig? config)
    {
        if (config == null) return null;
        
        return new FreeTrialCodeBatchConfig
        {
            TrialDays = config.TrialDays,
            ProductId = config.ProductId,
            PlanType = (QuotaPlanType)config.PlanType,
            IsUltimate = config.IsUltimate,
            Platform = (PaymentPlatform)config.Platform,
            StartTime = config.StartTime?.ToDateTime() ?? DateTime.MinValue,
            EndTime = config.EndTime?.ToDateTime() ?? DateTime.MaxValue,
            Description = config.Description
        };
    }

    #region EventHandlers

    [EventHandler]
    public void HandleInitializeFactoryEvent(InitializeFactoryEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleGenerateCodesEvent(GenerateCodesEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleMarkCodeUsedEvent(MarkCodeUsedEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleUpdateFactoryStatusEvent(UpdateFactoryStatusEvent @event)
    {
        TransitionState(State, @event);
    }

    #endregion

    protected override void TransitionState(FreeTrialCodeFactoryState state, IMessage evt)
    {
        switch (evt)
        {
            case InitializeFactoryEvent initEvent:
                state.BatchId = initEvent.BatchId;
                state.OperatorUserId = initEvent.OperatorUserId;
                state.BatchConfig = initEvent.BatchConfig;
                state.CreationTime = initEvent.CreationTime;
                state.Status = initEvent.Status;
                break;

            case GenerateCodesEvent generateEvent:
                state.GeneratedCodes.Clear();
                state.GeneratedCodes.AddRange(generateEvent.GeneratedCodes);
                state.TotalCodesGenerated = generateEvent.Quantity;
                state.LastGenerationTime = generateEvent.CreationTime;
                state.Status = generateEvent.Status;
                break;

            case MarkCodeUsedEvent usedEvent:
                state.UsedCount++;
                state.UsedCodes.Add(usedEvent.Code);
                break;

            case UpdateFactoryStatusEvent statusEvent:
                state.Status = statusEvent.Status;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }
}
