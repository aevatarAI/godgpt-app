using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.UserFeedback.Dtos;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// User Feedback service - manages user feedback collection and frequency control
/// </summary>
public interface IUserFeedbackService
{
    /// <summary>
    /// Submit user feedback
    /// </summary>
    Task<SubmitFeedbackResult> SubmitFeedbackAsync(Guid userId, SubmitFeedbackRequest request);
    
    /// <summary>
    /// Check if user is eligible to submit feedback
    /// </summary>
    Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync(Guid userId);
    
    /// <summary>
    /// Get feedback history with pagination
    /// </summary>
    Task<GetFeedbackHistoryResult> GetFeedbackHistoryAsync(Guid userId, GetFeedbackHistoryRequest request);
}
