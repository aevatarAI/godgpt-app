using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Core.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
// Alias to avoid conflict with Proto enums - use Proto types for State operations
using InvitationCodeType = Aevatar.Agents.GodGPT.Protos.InviteCode.InvitationCodeType;
using CsInvitationCodeType = Aevatar.Application.Grains.Common.Constants.InvitationCodeType;
using CsPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using CsPaymentPlatform = Aevatar.Application.Grains.Common.Constants.PaymentPlatform;

namespace Aevatar.Application.Grains.Agents.Invitation;

[GAgent(nameof(InviteCodeGAgent))]
public class InviteCodeGAgent : GAgentBase<InviteCodeState>, IInviteCodeGAgent
{
    public InviteCodeGAgent() : base()
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

    public async Task<ValidateInviteCodeResponse> ValidateAndGetInviterAsync()
    {
        if (!State.IsActive || string.IsNullOrEmpty(State.InviterId))
        {
            return new ValidateInviteCodeResponse
            {
                IsValid = false,
                InviterId = string.Empty
            };
        }

        RaiseEvent(new IncrementUsageCountEvent());
        await ConfirmEventsAsync();

        return new ValidateInviteCodeResponse
        {
            IsValid = true,
            InviterId = State.InviterId
        };
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

    public async Task<bool> InitializeFreeTrialCodeAsync(FreeTrialCodeInitProto initDto)
    {
        if (!string.IsNullOrWhiteSpace(State.InviteCode))
        {
            Logger.LogWarning("InviteCodeGAgent already initialized. {Code}", initDto.FreeTrialCode);
            return false;
        }
        
        RaiseEvent(new InitializeFreeTrialCodeEvent
        {
            Code = initDto.FreeTrialCode,
            BatchId = initDto.BatchId,
            TrialDays = initDto.TrialDays,
            ProductId = initDto.ProductId,
            PlanType = initDto.PlanType,
            IsUltimate = initDto.IsUltimate,
            StartDate = initDto.StartDate,
            EndDate = initDto.EndDate,
            InviteeId = initDto.InviteeId,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            IsActive = true,
            Platform = initDto.Platform,
            SessionUrl = initDto.SessionUrl,
            SessionExpiresAt = initDto.SessionExpiresAt
        });

        await ConfirmEventsAsync();
        
        Logger.LogInformation("Free trial code initialized. BatchId: {BatchId}, TrialDays: {TrialDays}", 
            initDto.BatchId, initDto.TrialDays);
        
        return true;
    }

    public Task<ValidateCodeResultProto> ValidateAndGetFreeTrialCodeInfoAsync(string inviteeId)
    {
        if (string.IsNullOrWhiteSpace(State.InviteCode))
        {
            return Task.FromResult(new ValidateCodeResultProto
            {
                IsValid = true,
                Message = string.Empty,
                CodeType = InvitationCodeType.FreeTrialReward
            });
        } 
        
        if (State.CodeType != InvitationCodeType.FreeTrialReward)
        {
            return Task.FromResult(new ValidateCodeResultProto
            {
                IsValid = false,
                Message = "Invalid code type",
                CodeType = State.CodeType
            });
        }

        if (!State.IsActive)
        {
            return Task.FromResult(new ValidateCodeResultProto
            {
                IsValid = false,
                Message = "Code is not active",
                CodeType = State.CodeType
            });
        }

        if (State.InviteeId != inviteeId)
        {
            return Task.FromResult(new ValidateCodeResultProto
            {
                IsValid = false,
                Message = "Code already used by another user",
                CodeType = State.CodeType
            });
        }

        try
        {
            var activationInfo = new FreeTrialActivationProto
                {
                CreatedAt = State.CreatedAt ?? Timestamp.FromDateTime(DateTime.MinValue),
                    IsActive = State.IsActive,
                    UsageCount = State.UsageCount,
                    InviteCode = State.InviteCode,
                CodeType = State.CodeType,
                    BatchId = State.BatchId,
                    TrialDays = State.TrialDays,
                    ProductId = State.ProductId,
                PlanType = State.PlanType,
                    IsUltimate = State.IsUltimate,
                Platform = State.Platform,
                    InviteeId = State.InviteeId,
                    SessionUrl = State.SessionUrl,
                SessionExpiresAt = State.SessionExpiresAt
            };
            
            if (State.UsedAt != null)
            {
                activationInfo.UsedAt = State.UsedAt;
            }
            
            return Task.FromResult(new ValidateCodeResultProto
            {
                IsValid = true,
                Message = string.Empty,
                CodeType = State.CodeType,
                ActivationInfo = activationInfo
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error redeeming free trial code for user {UserId}", inviteeId);
            return Task.FromResult(new ValidateCodeResultProto
            {
                IsValid = false,
                Message = "Internal error occurred",
                CodeType = State.CodeType
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

    public Task<FreeTrialCodeInfoProto> GetCodeInfoAsync()
    {
        var codeInfo = new FreeTrialCodeInfoProto
        {
            BatchId = State.BatchId,
            TrialDays = State.TrialDays,
            PlanType = State.PlanType,
            IsUltimate = State.IsUltimate
        };
        
        if (State.CodeType == InvitationCodeType.FreeTrialReward && State.UsedAt != null)
        {
            codeInfo.UsedAt = State.UsedAt;
        }

        return Task.FromResult(codeInfo);
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
                state.CodeType = InvitationCodeType.FriendInvitation;
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
                state.CodeType = InvitationCodeType.FreeTrialReward;
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
