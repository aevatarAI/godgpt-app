using System.Text;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.Invitation;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserQuota;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CsPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using CsRewardTypeEnum = Aevatar.Application.Grains.Common.Constants.RewardTypeEnum;
using CsMembershipLevel = Aevatar.Application.Grains.Common.Constants.MembershipLevel;

namespace Aevatar.Application.Grains.Invitation;

[GAgent(nameof(InvitationGAgent))]
public class InvitationGAgent : GAgentBase<InvitationState>, IInvitationGAgent
{
    private readonly DateTime DefaultIssueAt = new DateTime(2025, 7, 8, 0, 0, 0, DateTimeKind.Utc);
    private readonly IGAgentFactory _agentFactory;

    public InvitationGAgent(Guid id, IGAgentFactory agentFactory) : base(id)
    {
        _agentFactory = agentFactory;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Invitation Management GAgent");
    }

    public async Task<string> GenerateInviteCodeAsync()
    {
        if (!string.IsNullOrEmpty(State.CurrentInviteCode))
        {
            return State.CurrentInviteCode;
        }

        var inviteCode = await GenerateUniqueCodeAsync();
        var inviteCodeGrain = await GetInviteCodeAgentAsync(CommonHelper.StringToGuid(inviteCode));
        await inviteCodeGrain.InitializeAsync(Id.ToString(), inviteCode);

        RaiseEvent(new SetInviteCodeEvent
        {
            InviteCode = inviteCode,
            InviterId = Id.ToString()
        });
        await ConfirmEventsAsync();

        return inviteCode;
    }

    public async Task<InvitationStatsDto> GetInvitationStatsAsync()
    {
        // Twitter integration removed - no longer used
        return new InvitationStatsDto
        {
            TotalInvites = State.TotalInvites,
            ValidInvites = State.ValidInvites,
            PendingInvites = State.Invitees.Count(x => !x.Value.IsValid),
            TotalCreditsEarned = State.TotalCreditsEarned,
            InviteCode = State.CurrentInviteCode,
            TotalCreditsFromX = State.TotalCreditsFromX,
            IsBound = false  // Twitter binding removed
        };
    }

    public Task<List<RewardTierDto>> GetRewardTiersAsync()
    {
        var currentInvites = State.ValidInvites;
        var tiers = new List<RewardTierDto>();

        int currentLevel;
        if (currentInvites == 0)
        {
            currentLevel = 0;
        }
        else if (currentInvites == 1)
        {
            currentLevel = 1;
        }
        else
        {
            currentLevel = 1 + (currentInvites - 1) / 3;
        }

        const int totalLevels = 6;
        int startLevel;

        if (currentLevel <= 4)
        {
            startLevel = 1;
        }
        else
        {
            startLevel = currentLevel - 3;
        }

        for (int i = 0; i < totalLevels; i++)
        {
            int level = startLevel + i;
            int inviteCount = level == 1 ? 1 : 1 + (level - 1) * 3;
            tiers.Add(new RewardTierDto
            {
                InviteCount = inviteCount,
                Credits = level == 1 ? 30 : 100,
                IsCompleted = level <= currentLevel
            });
        }

        return Task.FromResult(tiers);
    }

    public Task<List<RewardHistoryDto>> GetRewardHistoryAsync()
    {
        return Task.FromResult(State.RewardHistory.Select(r => new RewardHistoryDto
        {
            InviteeId = r.InviteeId,
            Credits = r.Credits,
            RewardType = ((CsRewardTypeEnum)r.RewardType).ToString(),
            IssuedAt = r.IssuedAt?.ToDateTime() ?? DateTime.MinValue,
            IsScheduled = r.IsScheduled,
            ScheduledDate = r.ScheduledDate != null ? r.ScheduledDate.ToDateTime() : null,
            InvoiceId = r.InvoiceId
        }).ToList());
    }

    public Task<PagedResultDto<RewardHistoryDto>> GetRewardHistoryAsync(GetRewardHistoryRequestDto request)
    {
        var query = State.RewardHistory.AsEnumerable();

        // Filter out scheduled rewards
        query = query.Where(r => !r.IsScheduled);

        // Apply filter
        if (request.RewardType.HasValue)
        {
            var protoRewardType = (RewardType)request.RewardType.Value;
            query = query.Where(r => r.RewardType == protoRewardType);
        }

        // Get total count
        var filteredList = query.ToList();
        var totalCount = filteredList.Count;

        // Apply pagination
        var items = filteredList
            .OrderByDescending(r => r.IssuedAt?.ToDateTime() ?? DateTime.MinValue)
            .Skip((request.PageNo - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new RewardHistoryDto
            {
                InviteeId = r.InviteeId,
                Credits = r.Credits,
                RewardType = ((CsRewardTypeEnum)r.RewardType).ToString(),
                IssuedAt = r.IssuedAt?.ToDateTime() ?? DateTime.MinValue,
                IsScheduled = r.IsScheduled,
                ScheduledDate = r.ScheduledDate != null ? r.ScheduledDate.ToDateTime() : null,
                InvoiceId = r.InvoiceId,
                TweetId = r.TweetId
            })
            .ToList();

        return Task.FromResult(new PagedResultDto<RewardHistoryDto>(
            items,
            totalCount,
            request.PageNo,
            request.PageSize
        ));
    }

    public async Task ProcessScheduledRewardAsync()
    {
        var utcNow = DateTime.UtcNow;
        var scheduledRewards = State.RewardHistory.Where(r => r.IsScheduled &&
                                                              r.ScheduledDate != null &&
                                                              utcNow > r.ScheduledDate.ToDateTime() &&
                                                              !string.IsNullOrEmpty(r.InvoiceId)).ToList();
        if (scheduledRewards.Any())
        {
            var userQuotaGAgent = await GetUserQuotaAgentAsync(Id);
            foreach (var reward in scheduledRewards)
            {
                Logger.LogInformation(
                    $"[InvitationGAgent][ProcessScheduledRewardAsync] Processing scheduled reward for user {Id}, credits: {reward.Credits}");
                await userQuotaGAgent.AddCreditsAsync(reward.Credits);
                await MarkRewardAsIssuedAsync(reward.InviteeId, reward.InvoiceId);
            }
        }
    }

    public async Task<bool> ProcessInviteeRegistrationAsync(string inviteeId)
    {
        if (State.Invitees.ContainsKey(inviteeId))
        {
            return false;
        }

        RaiseEvent(new AddInviteeEvent
        {
            InviteeId = inviteeId,
            InvitedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
        return true;
    }

    public async Task ProcessInviteeChatCompletionAsync(string inviteeId)
    {
        if (!State.Invitees.TryGetValue(inviteeId, out var invitee) || invitee.HasCompletedChat)
        {
            return;
        }

        RaiseEvent(new UpdateInviteeStatusEvent
        {
            InviteeId = inviteeId,
            HasCompletedChat = true,
            HasPaid = invitee.HasPaid,
            PaidPlan = invitee.PaidPlan,
            PaidAt = invitee.PaidAt != null ? invitee.PaidAt : null
        });
        await ConfirmEventsAsync();

        // Issue first invite reward if this is the first valid invite
        if (State.ValidInvites == 0)
        {
            await IssueReward(inviteeId, 30, RewardType.FirstInviteReward);
        }
        // Issue group reward if completing a group of 3
        else if ((State.ValidInvites) % 3 == 0)
        {
            await IssueReward(inviteeId, 100, RewardType.GroupInviteReward);
        }

        RaiseEvent(new UpdateValidInvitesEvent { ValidInvites = State.ValidInvites + 1 });
        await ConfirmEventsAsync();
    }

    public async Task ProcessInviteeSubscriptionAsync(string inviteeId, CsPlanType planType, bool isUltimate,
        string invoiceId)
    {
        if (!State.Invitees.TryGetValue(inviteeId, out var invitee) || invitee.HasPaid)
        {
            return;
        }

        var credits = GetSubscriptionRewardCredits(planType, isUltimate);
        if (credits <= 0)
        {
            return;
        }

        RaiseEvent(new UpdateInviteeStatusEvent
        {
            InviteeId = inviteeId,
            HasCompletedChat = invitee.HasCompletedChat,
            HasPaid = true,
            PaidPlan = (InvitationPlanType)planType,
            PaidAt = Timestamp.FromDateTime(DateTime.UtcNow),
            MembershipLevel = isUltimate
                ? CsMembershipLevel.Membership_Level_Ultimate
                : CsMembershipLevel.Membership_Level_Premium
        });
        await ConfirmEventsAsync();

        // For annual plans, schedule the reward for 30 days later
        if (planType == CsPlanType.Year)
        {
            var addRewardEvent = new AddRewardEvent
            {
                InviteeId = inviteeId,
                Credits = credits,
                RewardType = RewardType.SubscriptionReward,
                IsScheduled = true,
                ScheduledDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(30)),
                InvoiceId = invoiceId,
                IssueAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            RaiseEvent(addRewardEvent);
            await ConfirmEventsAsync();
            Logger.LogInformation($"Scheduled reward of {credits} credits for invitee {inviteeId} in 30 days {addRewardEvent.ScheduledDate}");
        }
        else
        {
            await IssueReward(inviteeId, credits, RewardType.SubscriptionReward);
        }
    }

    private async Task IssueReward(string inviteeId, int credits, RewardType rewardType)
    {
        var userQuotaGAgent = await GetUserQuotaAgentAsync(Id);
        await userQuotaGAgent.AddCreditsAsync(credits);

        RaiseEvent(new AddRewardEvent
        {
            InviteeId = inviteeId,
            Credits = credits,
            RewardType = rewardType,
            IssueAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        await ConfirmEventsAsync();
    }

    private int GetSubscriptionRewardCredits(CsPlanType planType, bool isUltimate)
    {
        if (isUltimate)
        {
            return planType switch
            {
                CsPlanType.Week => 500,
                CsPlanType.Month => 2000,
                CsPlanType.Year => 20000,
                _ => 0
            };
        }
        else
        {
            return planType switch
            {
                CsPlanType.Week => 100,
                CsPlanType.Month => 400,
                CsPlanType.Year => 4000,
                _ => 0
            };
        }
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int attemptCount = 0;
        while (true)
        {
            attemptCount++;
            string code = ToBase62(timestamp);
            var codeGrainId = CommonHelper.StringToGuid(code);
            var codeGrain = await GetInviteCodeAgentAsync(codeGrainId);
            var isUsed = await codeGrain.IsInitialized();

            Logger.LogDebug(
                $"[InvitationGAgent][GenerateUniqueCodeAsync] Attempt {attemptCount}: Generated invite code {code}, isUsed: {isUsed}");

            if (!isUsed)
            {
                return code;
            }

            timestamp++;
        }
    }

    private string ToBase62(long number)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sb = new StringBuilder();
        if (number == 0)
            return "0";
        while (number > 0)
        {
            sb.Insert(0, chars[(int)(number % 62)]);
            number /= 62;
        }

        return sb.ToString();
    }

    #region EventHandlers

    [EventHandler]
    public void HandleSetInviteCodeEvent(SetInviteCodeEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleAddInviteeEvent(AddInviteeEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleUpdateInviteeStatusEvent(UpdateInviteeStatusEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleAddRewardEvent(AddRewardEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleUpdateValidInvitesEvent(UpdateValidInvitesEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleMarkRewardIssuedEvent(MarkRewardIssuedEvent @event)
    {
        TransitionState(State, @event);
    }

    #endregion

    protected override void TransitionState(InvitationState state, IMessage evt)
    {
        switch (evt)
        {
            case SetInviteCodeEvent setInviteCode:
                state.InviterId = setInviteCode.InviterId;
                state.CurrentInviteCode = setInviteCode.InviteCode;
                break;

            case AddInviteeEvent addInvitee:
                state.Invitees[addInvitee.InviteeId] = new InviteeInfo
                {
                    InviteeId = addInvitee.InviteeId,
                    InvitedAt = addInvitee.InvitedAt
                };
                state.TotalInvites++;
                break;

            case UpdateInviteeStatusEvent updateStatus:
                if (state.Invitees.TryGetValue(updateStatus.InviteeId, out var invitee))
                {
                    invitee.HasCompletedChat = updateStatus.HasCompletedChat;
                    invitee.HasPaid = updateStatus.HasPaid;
                    invitee.PaidPlan = updateStatus.PaidPlan;
                    if (updateStatus.PaidAt != null)
                    {
                        invitee.PaidAt = updateStatus.PaidAt;
                    }
                    invitee.IsValid = updateStatus.HasCompletedChat;
                }
                break;

            case AddRewardEvent addReward:
                var rewardRecord = new RewardRecord
                {
                    InviteeId = addReward.InviteeId,
                    Credits = addReward.Credits,
                    RewardType = addReward.RewardType,
                    IssuedAt = addReward.IssueAt ?? Timestamp.FromDateTime(DefaultIssueAt),
                    IsScheduled = addReward.IsScheduled,
                    InvoiceId = addReward.InvoiceId ?? string.Empty,
                    TweetId = addReward.TweetId ?? string.Empty
                };
                if (addReward.ScheduledDate != null)
                {
                    rewardRecord.ScheduledDate = addReward.ScheduledDate;
                }

                state.RewardHistory.Add(rewardRecord);
                if (!addReward.IsScheduled)
                {
                    if (addReward.RewardType == RewardType.TwitterReward)
                    {
                        state.TotalCreditsFromX += addReward.Credits;
                    }
                    else
                    {
                        state.TotalCreditsEarned += addReward.Credits;
                    }
                }
                break;

            case UpdateValidInvitesEvent updateValidInvites:
                state.ValidInvites = updateValidInvites.ValidInvites;
                break;

            case MarkRewardIssuedEvent markIssued:
                var reward = state.RewardHistory.FirstOrDefault(r =>
                    r.InviteeId == markIssued.InviteeId &&
                    r.InvoiceId == markIssued.InvoiceId &&
                    r.IsScheduled);

                if (reward != null)
                {
                    reward.IsScheduled = false;
                    state.TotalCreditsEarned += reward.Credits;
                }
                break;

            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }

    private async Task MarkRewardAsIssuedAsync(string inviteeId, string invoiceId)
    {
        var reward = State.RewardHistory.FirstOrDefault(r =>
            r.InviteeId == inviteeId &&
            r.InvoiceId == invoiceId &&
            r.IsScheduled);

        if (reward != null)
        {
            RaiseEvent(new MarkRewardIssuedEvent
            {
                InviteeId = inviteeId,
                InvoiceId = invoiceId
            });
            await ConfirmEventsAsync();
        }
        else
        {
            Logger.LogWarning(
                $"[InvitationGAgent][MarkRewardAsIssuedAsync] reward not found. userId {Id}, inviteeId {inviteeId}, invoiceId {inviteeId}");
        }
    }

    public async Task<bool> ProcessTwitterRewardAsync(string tweetId, int credits)
    {
        Logger.LogDebug(
            $"[InvitationGAgent][ProcessTwitterRewardAsync] process twitter reward. userId {Id}, tweetId {tweetId}, credits {credits}");
        // Issue the reward
        var userQuotaGAgent = await GetUserQuotaAgentAsync(Id);
        await userQuotaGAgent.AddCreditsAsync(credits);

        // Record the reward
        RaiseEvent(new AddRewardEvent
        {
            InviteeId = Id.ToString(), // Use the current user's ID
            Credits = credits,
            RewardType = RewardType.TwitterReward,
            IsScheduled = false,
            TweetId = tweetId,
            IssueAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        await ConfirmEventsAsync();

        return true;
    }

    private async Task<InviteCodeGAgent> GetInviteCodeAgentAsync(Guid codeGrainId)
    {
        var agent = _agentFactory.CreateGAgent<InviteCodeGAgent>(codeGrainId);
        await agent.ActivateAsync();
        return agent;
    }
    
    private async Task<UserQuotaGAgent> GetUserQuotaAgentAsync(Guid userId)
    {
        var agent = _agentFactory.CreateGAgent<UserQuotaGAgent>(userId);
        await agent.ActivateAsync();
        return agent;
    }
}
