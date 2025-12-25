using System;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.UserFeedback;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.UserFeedback;
using Aevatar.Application.Grains.UserFeedback.Dtos;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.App.Application.Contracts.Services;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Application.Services;

/// <summary>
/// User Feedback service implementation - manages user feedback collection and frequency control
/// Uses UserFeedbackGAgent architecture
/// </summary>
public class UserFeedbackService : IUserFeedbackService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<UserFeedbackService> _logger;

    public UserFeedbackService(
        IGAgentActorFactory actorFactory,
        ILogger<UserFeedbackService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    private async Task<IUserFeedbackGAgent> GetAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserFeedbackGAgent>(userId.ToString());
        return actor.As<IUserFeedbackGAgent>();
    }

    public async Task<SubmitFeedbackResult> SubmitFeedbackAsync(Guid userId, SubmitFeedbackRequest request)
    {
        _logger.LogInformation("[UserFeedbackService] Submitting feedback for user {UserId}, type {FeedbackType}", 
            userId, request.FeedbackType);
        
        var agent = await GetAgentAsync(userId);
        
        // Convert DTO to Protobuf
        var protoRequest = new SubmitFeedbackRequestProto
        {
            UserId = userId.ToString(),
            FeedbackType = request.FeedbackType,
            Response = request.Response ?? string.Empty,
            ContactRequested = request.ContactRequested,
            Email = request.Email ?? string.Empty,
            SkippedFeedback = request.SkippedFeedback
        };
        
        // Convert reasons (enum values)
        foreach (var reason in request.Reasons)
        {
            protoRequest.Reasons.Add((int)reason);
        }
        
        // Convert subscription if present
        if (request.Subscription != null)
        {
            protoRequest.Subscription = new UserSubscriptionInfoProto
            {
                PlanType = (int)request.Subscription.PlanType,
                IsUltimate = request.Subscription.IsUltimate,
                StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(request.Subscription.StartDate, DateTimeKind.Utc)),
                EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(request.Subscription.EndDate, DateTimeKind.Utc))
            };
        }
        
        var protoResult = await agent.SubmitFeedbackAsync(protoRequest);
        
        // Convert Protobuf to DTO
        return new SubmitFeedbackResult
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            ErrorCode = string.IsNullOrEmpty(protoResult.ErrorCode) ? null : protoResult.ErrorCode
        };
    }

    public async Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync(Guid userId)
    {
        _logger.LogDebug("[UserFeedbackService] Checking feedback eligibility for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        var protoResult = await agent.CheckFeedbackEligibilityAsync();
        
        // Convert Protobuf to DTO
        return new CheckEligibilityResult
        {
            Eligible = protoResult.Eligible,
            LastFeedbackTime = protoResult.LastFeedbackTime?.ToDateTime(),
            NextEligibleTime = protoResult.NextEligibleTime?.ToDateTime(),
            Message = protoResult.Message
        };
    }

    public async Task<GetFeedbackHistoryResult> GetFeedbackHistoryAsync(Guid userId, GetFeedbackHistoryRequest request)
    {
        _logger.LogDebug("[UserFeedbackService] Getting feedback history for user {UserId}, pageSize {PageSize}, pageIndex {PageIndex}", 
            userId, request.PageSize, request.PageIndex);
        
        var agent = await GetAgentAsync(userId);
        
        // Convert DTO to Protobuf
        var protoRequest = new GetFeedbackHistoryRequestProto
        {
            PageSize = request.PageSize,
            PageIndex = request.PageIndex
        };
        
        var protoResult = await agent.GetFeedbackHistoryAsync(protoRequest);
        
        // Convert Protobuf to DTO
        var result = new GetFeedbackHistoryResult
        {
            TotalCount = protoResult.TotalCount,
            HasMore = protoResult.HasMore
        };
        
        foreach (var protoItem in protoResult.Feedbacks)
        {
            var item = new FeedbackHistoryItem
            {
                FeedbackId = protoItem.FeedbackId,
                FeedbackType = protoItem.FeedbackType,
                Response = protoItem.Response,
                ContactRequested = protoItem.ContactRequested,
                Email = protoItem.Email,
                SubmittedAt = protoItem.SubmittedAt.ToDateTime()
            };
            
            // Convert reasons
            foreach (var reasonValue in protoItem.Reasons)
            {
                item.Reasons.Add((Aevatar.Application.Grains.Common.Constants.FeedbackReasonEnum)reasonValue);
            }
            
            // Convert subscription if present
            if (protoItem.Subscription != null)
            {
                item.Subscription = new UserSubscription
                {
                    PlanType = (QuotaPlanType)protoItem.Subscription.PlanType,
                    IsUltimate = protoItem.Subscription.IsUltimate,
                    StartDate = protoItem.Subscription.StartDate.ToDateTime(),
                    EndDate = protoItem.Subscription.EndDate.ToDateTime()
                };
            }
            
            result.Feedbacks.Add(item);
        }
        
        return result;
    }
}

