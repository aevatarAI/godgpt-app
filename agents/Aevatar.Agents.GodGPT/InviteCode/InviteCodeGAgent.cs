using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Core.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

// Using Proto types from invite_code.proto and user_quota.proto
using PaymentPlatform = Aevatar.Application.Grains.Common.Constants.PaymentPlatform;

namespace Aevatar.Application.Grains.Agents.Invitation;

[GAgent(nameof(InviteCodeGAgent))]
public class InviteCodeGAgent : GAgentBase<InviteCodeState>, IInviteCodeGAgent
{
    public InviteCodeGAgent(Guid id) : base(id)
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Invite Code Management GAgent");
    }

    public async Task<bool> InitializeAsync(string inviterId, string inviteCode)
    {
        if (!string.IsNullOrEmpty(State.InviterId))
        {
            return false;
        }

        RaiseEvent(new InitializeInviteCodeEvent
        {
            InviterId = inviterId,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            InviteCode = inviteCode
        });

        await ConfirmEventsAsync();
        return true;
    }

    public async Task<(bool isValid, string inviterId)> ValidateAndGetInviterAsync()
    {
        if (!State.IsActive || string.IsNullOrEmpty(State.InviterId))
        {
            return (false, string.Empty);
        }

        RaiseEvent(new IncrementUsageCountEvent());
        await ConfirmEventsAsync();

        return (true, State.InviterId);
    }

    public Task<bool> IsInitialized()
    {
        return Task.FromResult(!string.IsNullOrEmpty(State.InviterId));
    }

    public async Task DeactivateCodeAsync()
    {
        if (!State.IsActive)
        {
            return;
        }

        RaiseEvent(new DeactivateInviteCodeEvent());
        await ConfirmEventsAsync();
    }

    public async Task<bool> InitializeFreeTrialCodeAsync(FreeTrialCodeInitDto initDto)
    {
        if (!string.IsNullOrWhiteSpace(State.InviteCode))
        {
            Logger.LogWarning("InviteCodeGAgent already initialized. {Code}", initDto.FreeTrialCode);
            return false;
        }
        
        RaiseEvent(new InitializeFreeTrialCodeEvent
        {
            Code = initDto.FreeTrialCode ?? string.Empty,
            BatchId = initDto.BatchId,
            TrialDays = initDto.TrialDays,
            ProductId = initDto.ProductId ?? string.Empty,
            PlanType = initDto.PlanType,
            IsUltimate = initDto.IsUltimate,
            StartDate = Timestamp.FromDateTime(initDto.StartDate.ToUniversalTime()),
            EndDate = Timestamp.FromDateTime(initDto.EndDate.ToUniversalTime()),
            InviteeId = initDto.InviteeId ?? string.Empty,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            IsActive = true,
            Platform = (int)initDto.Platform,
            SessionUrl = initDto.SessionUrl ?? string.Empty,
            SessionExpiresAt = Timestamp.FromDateTime(initDto.SessionExpiresAt.ToUniversalTime())
        });

        await ConfirmEventsAsync();
        
        Logger.LogInformation("Free trial code initialized. BatchId: {BatchId}, TrialDays: {TrialDays}", 
            initDto.BatchId, initDto.TrialDays);
        
        return true;
    }

    public Task<ValidateCodeResultDto> ValidateAndGetFreeTrialCodeInfoAsync(string inviteeId)
    {
        if (string.IsNullOrWhiteSpace(State.InviteCode))
        {
            return Task.FromResult(new ValidateCodeResultDto
            {
                IsValid = true,
                Message = string.Empty,
                CodeType = InviteCodeType.FreeTrialReward,
                ActivationInfo = null
            });
        } 
        
        if (State.CodeType != InviteCodeType.FreeTrialReward)
        {
            return Task.FromResult(new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Invalid code type",
                CodeType = State.CodeType,
                ActivationInfo = null
            });
        }

        if (!State.IsActive)
        {
            return Task.FromResult(new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Code is not active",
                CodeType = State.CodeType,
                ActivationInfo = null
            });
        }

        if (State.InviteeId != inviteeId)
        {
            return Task.FromResult(new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Code already used by another user",
                CodeType = State.CodeType,
                ActivationInfo = null
            });
        }

        try
        {
            return Task.FromResult(new ValidateCodeResultDto
            {
                IsValid = true,
                Message = string.Empty,
                CodeType = State.CodeType,
                ActivationInfo = new FreeTrialActivationDto
                {
                    CreatedAt = State.CreatedAt?.ToDateTime() ?? DateTime.MinValue,
                    IsActive = State.IsActive,
                    UsageCount = State.UsageCount,
                    InviteCode = State.InviteCode,
                    CodeType = State.CodeType,
                    BatchId = State.BatchId,
                    TrialDays = State.TrialDays,
                    ProductId = State.ProductId,
                    PlanType = State.PlanType,
                    IsUltimate = State.IsUltimate,
                    Platform = (PaymentPlatform)State.Platform,
                    InviteeId = State.InviteeId,
                    UsedAt = State.UsedAt?.ToDateTime(),
                    SessionUrl = State.SessionUrl,
                    SessionExpiresAt = State.SessionExpiresAt?.ToDateTime() ?? DateTime.MinValue
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error redeeming free trial code for user {UserId}", inviteeId);
            return Task.FromResult(new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Internal error occurred",
                CodeType = State.CodeType,
                ActivationInfo = null
            });
        }
    }

    public async Task<bool> MarkCodeAsUsedAsync()
    {
        RaiseEvent(new MarkCodeAsUsedEvent
        {
            IsActive = false,
            UsedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
        
        Logger.LogInformation("Free trial code Used. BatchId: {BatchId}, InviteCode: {InviteCode}", 
            State.BatchId, State.InviteCode);
        return true;
    }

    public Task<FreeTrialCodeInfoDto?> GetCodeInfoAsync()
    {
        if (State.CodeType != InviteCodeType.FreeTrialReward)
        {
            return Task.FromResult<FreeTrialCodeInfoDto?>(null);
        }

        var codeInfo = new FreeTrialCodeInfoDto
        {
            BatchId = State.BatchId,
            TrialDays = State.TrialDays,
            PlanType = State.PlanType,
            IsUltimate = State.IsUltimate,
            UsedAt = State.UsedAt?.ToDateTime()
        };

        return Task.FromResult<FreeTrialCodeInfoDto?>(codeInfo);
    }

    #region EventHandlers

    [EventHandler]
    public void HandleInitializeInviteCodeEvent(InitializeInviteCodeEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleDeactivateInviteCodeEvent(DeactivateInviteCodeEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleIncrementUsageCountEvent(IncrementUsageCountEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleInitializeFreeTrialCodeEvent(InitializeFreeTrialCodeEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleMarkCodeAsUsedEvent(MarkCodeAsUsedEvent @event)
    {
        TransitionState(State, @event);
    }

    #endregion

    protected override void TransitionState(InviteCodeState state, IMessage evt)
    {
        switch (evt)
        {
            case InitializeInviteCodeEvent initEvent:
                state.InviterId = initEvent.InviterId;
                state.CreatedAt = initEvent.CreatedAt;
                state.IsActive = true;
                state.UsageCount = 0;
                state.CodeType = InviteCodeType.FriendInvitation;
                state.InviteCode = initEvent.InviteCode;
                break;

            case DeactivateInviteCodeEvent:
                state.IsActive = false;
                break;
            
            case IncrementUsageCountEvent:
                state.UsageCount++;
                break;

            case InitializeFreeTrialCodeEvent freeTrialEvent:
                state.InviteCode = freeTrialEvent.Code;
                state.CreatedAt = freeTrialEvent.CreatedAt;
                state.IsActive = freeTrialEvent.IsActive;
                state.UsageCount = 1;
                state.CodeType = InviteCodeType.FreeTrialReward;
                state.BatchId = freeTrialEvent.BatchId;
                state.TrialDays = freeTrialEvent.TrialDays;
                state.ProductId = freeTrialEvent.ProductId;
                state.PlanType = freeTrialEvent.PlanType;
                state.IsUltimate = freeTrialEvent.IsUltimate;
                state.Platform = freeTrialEvent.Platform;
                state.InviteeId = freeTrialEvent.InviteeId;
                state.SessionUrl = freeTrialEvent.SessionUrl;
                state.SessionExpiresAt = freeTrialEvent.SessionExpiresAt;
                break;

            case MarkCodeAsUsedEvent redeemEvent:
                state.UsedAt = redeemEvent.UsedAt;
                state.IsActive = redeemEvent.IsActive;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }
}
