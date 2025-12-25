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
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IFreeTrialCodeFactoryGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    Task<GenerateCodesResultProto> GenerateCodesAsync(GenerateCodesRequestProto request);
    Task<BatchInfoProto> GetBatchInfoAsync();
    Task<bool> MarkCodeAsUsedAsync(MarkCodeUsedRequestProto request);
    Task<bool> ValidateCodeOwnershipAsync(ValidateCodeRequestProto request);
    Task<bool> ValidateCodeAvailableAsync(ValidateCodeRequestProto request);
}

[GAgent(nameof(FreeTrialCodeFactoryGAgent))]
public class FreeTrialCodeFactoryGAgent : GAgentBase<FreeTrialCodeFactoryState>, IFreeTrialCodeFactoryGAgent
{
    // Dependency injection via properties for Orleans compatibility
    public IOptionsMonitor<StripeOptions>? StripeOptions { get; set; }
    public IOptionsMonitor<CreditsOptions>? CreditsOptions { get; set; }

    private const int MaxQuantity = 10000;

    // Parameterless constructor required for Orleans activation
    public FreeTrialCodeFactoryGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Free Trial Code Factory Management GAgent");
    }

    public async Task<GenerateCodesResultProto> GenerateCodesAsync(GenerateCodesRequestProto request)
    {
        if (!IsUserAuthorizedToGenerateCode(request.OperatorUserId))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Unauthorized attempt to generate codes by user {OperatorUserId}",
                request.OperatorUserId);
            return new GenerateCodesResultProto
            {
                Success = false,
                Message = "Unauthorized attempt to generate code",
                ErrorCode = (int)FreeTrialCodeError.InternalError
            };
        }
        
        await InitializeFactoryAsync(request);

        if (!State.HasBatchId)
        {
            Logger.LogError(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Factory not initialized. OperatorUserId: {OperatorUserId}",
                request.OperatorUserId);
            return new GenerateCodesResultProto
            {
                Success = false,
                Message = "Factory not initialized",
                ErrorCode = (int)FreeTrialCodeError.InternalError
            };
        }

        if (State.Status != FactoryStatus.Active)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Factory is not active. BatchId: {BatchId}, Status: {Status}, OperatorUserId: {OperatorUserId}",
                State.BatchId, State.Status, request.OperatorUserId);
            return new GenerateCodesResultProto
            {
                Success = false,
                Message = $"Factory is not active. Status: {State.Status}",
                ErrorCode = (int)FreeTrialCodeError.InternalError
            };
        }

        if (request.Quantity > MaxQuantity)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Requested quantity exceeds maximum. BatchId: {BatchId}, Requested: {RequestedQuantity}, Max: {MaxQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, request.Quantity, MaxQuantity, request.OperatorUserId);
            return new GenerateCodesResultProto
            {
                Success = false,
                Message = $"Quantity cannot exceed batch max quantity: {MaxQuantity}",
                ErrorCode = (int)FreeTrialCodeError.InternalError
            };
        }

        if (State.TotalCodesGenerated + request.Quantity > MaxQuantity)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Would exceed batch capacity. BatchId: {BatchId}, Current: {CurrentGenerated}, Requested: {RequestedQuantity}, Max: {MaxQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, State.TotalCodesGenerated, request.Quantity, MaxQuantity, request.OperatorUserId);
            return new GenerateCodesResultProto
            {
                Success = false,
                Message =
                    $"Would exceed batch capacity. Current: {State.TotalCodesGenerated}, Requested: {request.Quantity}, Max: {MaxQuantity}",
                ErrorCode = (int)FreeTrialCodeError.InternalError
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

            var result = new GenerateCodesResultProto
            {
                Success = true,
                Message = "Codes generated successfully",
                GeneratedCount = codes.Count,
                ErrorCode = (int)FreeTrialCodeError.None
            };
            result.Codes.AddRange(codes);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Error generating codes. BatchId: {BatchId}, RequestedQuantity: {RequestedQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, request.Quantity, request.OperatorUserId);
            return new GenerateCodesResultProto
            {
                Success = false,
                Message = "Internal error occurred while generating codes",
                ErrorCode = (int)FreeTrialCodeError.InternalError
            };
        }
    }

    private async Task<bool> InitializeFactoryAsync(GenerateCodesRequestProto request)
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
            Platform = request.Platform,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Description = request.Description ?? string.Empty
        };

        RaiseEvent(new InitializeFactoryEvent
        {
            BatchId = request.BatchId,
            OperatorUserId = request.OperatorUserId,
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

    public Task<BatchInfoProto> GetBatchInfoAsync()
    {
        var batchInfo = new BatchInfoProto
        {
            BatchId = State.HasBatchId ? State.BatchId : 0,
            TotalGenerated = State.TotalCodesGenerated,
            UsedCount = State.UsedCount,
            CreationTime = State.CreationTime ?? Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)),
            LastGenerationTime = State.LastGenerationTime ?? Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)),
            Status = State.Status
        };

        if (State.BatchConfig != null)
        {
            batchInfo.Config = State.BatchConfig;
        }

        batchInfo.GeneratedCodes.AddRange(State.GeneratedCodes);
        batchInfo.UsedCodes.AddRange(State.UsedCodes);

        return Task.FromResult(batchInfo);
    }

    public async Task<bool> MarkCodeAsUsedAsync(MarkCodeUsedRequestProto request)
    {
        if (!await ValidateCodeOwnershipAsync(new ValidateCodeRequestProto { Code = request.Code }))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code not found in batch. Code: {Code}, BatchId: {BatchId}, UserId: {UserId}", 
                request.Code, State.BatchId, request.UserId);
            return false;
        }

        if (State.UsedCodes.Contains(request.Code))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code already used. Code: {Code}, BatchId: {BatchId}, UserId: {UserId}", 
                request.Code, State.BatchId, request.UserId);
            return false;
        }

        RaiseEvent(new MarkCodeUsedEvent
        {
            Code = request.Code,
            UserId = request.UserId,
            UsedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();

        Logger.LogInformation(
            "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code marked as used successfully. Code: {Code}, UserId: {UserId}, BatchId: {BatchId}",
            request.Code, request.UserId, State.BatchId);

        return true;
    }

    public Task<bool> ValidateCodeOwnershipAsync(ValidateCodeRequestProto request)
    {
        if (!State.HasBatchId)
        {
            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(request.Code)
            || State.GeneratedCodes.Count == 0
            || !InvitationCodeHelper.IsValidFreeTrialCodeFormat(request.Code))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (!InvitationCodeHelper.IsCodeFromBatch(request.Code, State.BatchId))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(State.GeneratedCodes.Contains(request.Code));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, 
                "[FreeTrialCodeFactoryGAgent][ValidateCodeOwnershipAsync] Error validating code ownership. Code: {Code}, BatchId: {BatchId}", 
                request.Code, State.BatchId);
            return Task.FromResult(false);
        }
    }

    public async Task<bool> ValidateCodeAvailableAsync(ValidateCodeRequestProto request)
    {
        if (!await ValidateCodeOwnershipAsync(request))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code not found in batch. Code: {Code}, BatchId: {BatchId}", 
                request.Code, State.BatchId);
            return false;
        }

        if (State.UsedCodes.Contains(request.Code))
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code already used. Code: {Code}, BatchId: {BatchId}", 
                request.Code, State.BatchId);
            return false;
        }

        if (State.BatchConfig == null)
        {
            Logger.LogWarning("[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] BatchConfig is null. Code: {Code}, BatchId: {BatchId}", 
                request.Code, State.BatchId);
            return false;
        }

        var currentTime = DateTime.UtcNow;
        var startTime = State.BatchConfig.StartTime?.ToDateTime().ToUniversalTime() ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var endTime = State.BatchConfig.EndTime?.ToDateTime().ToUniversalTime() ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

        if (currentTime < startTime)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code not yet valid. Code: {Code}, BatchId: {BatchId}, Current: {CurrentTime}, Start: {StartTime}",
                request.Code, State.BatchId, currentTime, startTime);
            return false;
        }

        if (currentTime > endTime)
        {
            Logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code has expired. Code: {Code}, BatchId: {BatchId}, Current: {CurrentTime}, End: {EndTime}",
                request.Code, State.BatchId, currentTime, endTime);
            return false;
        }

        Logger.LogDebug(
            "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code is available for use. Code: {Code}, BatchId: {BatchId}", 
            request.Code, State.BatchId);
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
        if (CreditsOptions == null)
        {
            Logger.LogError("[FreeTrialCodeFactoryGAgent] CreditsOptions is not injected");
            return false;
        }
        var authorizedUsers = CreditsOptions.CurrentValue.OperatorUserId;
        return authorizedUsers.Contains(operatorUserId);
    }

    private Task<StripeProduct> GetStripeProductConfigAsync(string priceId)
    {
        if (StripeOptions == null)
        {
            Logger.LogError("[FreeTrialCodeFactoryGAgent] StripeOptions is not injected");
            throw new InvalidOperationException("StripeOptions is not injected");
        }
        var productConfig = StripeOptions.CurrentValue.Products.FirstOrDefault(p => p.PriceId == priceId);
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
            Platform = (Aevatar.Application.Grains.Common.Constants.PaymentPlatform)config.Platform,
            StartTime = config.StartTime?.ToDateTime().ToUniversalTime() ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
            EndTime = config.EndTime?.ToDateTime().ToUniversalTime() ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
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
