using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.UserFeedback.Dtos;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.Dtos;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT user information collection and feedback.
/// Handles user onboarding data collection and user feedback submission.
/// </summary>
[RemoteService]
[ControllerName("GodGPTUserInfo")]
[Route("api/godgpt/userinfo")]
[Authorize]
public class GodGPTUserInfoController : AevatarController
{
    private readonly IUserInfoService _userInfoService;
    private readonly IUserFeedbackService _userFeedbackService;
    private readonly ILogger<GodGPTUserInfoController> _logger;
    private readonly IAgentContextAccessor _agentContextAccessor;

    public GodGPTUserInfoController(
        IUserInfoService userInfoService,
        IUserFeedbackService userFeedbackService,
        ILogger<GodGPTUserInfoController> logger,
        IAgentContextAccessor agentContextAccessor)
    {
        _userInfoService = userInfoService;
        _userFeedbackService = userFeedbackService;
        _logger = logger;
        _agentContextAccessor = agentContextAccessor;
    }

    /// <summary>
    /// Get user info options (seeking interests, source channels)
    /// </summary>
    [HttpGet("query-option")]
    public async Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        // Set language context from HTTP header for agent localization
        var language = HttpContext.GetGodGPTLanguage();
        var agentContext = _agentContextAccessor.GetOrCreate();
        agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());
        
        var response = await _userInfoService.GetUserInfoOptionsAsync(currentUserId);
        
        _logger.LogDebug("[GodGPTUserInfoController][GetUserInfoOptionsAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return response;
    }

    /// <summary>
    /// Get user information collection
    /// </summary>
    [HttpGet("query")]
    public async Task<UserInfoCollectionDto> GetUserInfoAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var response = await _userInfoService.GetUserInfoCollectionAsync(currentUserId);
        
        _logger.LogDebug("[GodGPTUserInfoController][GetUserInfoAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return response;
    }

    /// <summary>
    /// Collect/update user information collection
    /// </summary>
    [HttpPost("collect")]
    public async Task<UserInfoCollectionResponseDto> CollectUserInfoAsync(UpdateUserInfoCollectionDto userInput)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var response = await _userInfoService.UpdateUserInfoCollectionAsync(currentUserId, userInput);
        
        _logger.LogDebug("[GodGPTUserInfoController][CollectUserInfoAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return response;
    }

    /// <summary>
    /// Submit user feedback
    /// </summary>
    [HttpPost("feedback")]
    public async Task<SubmitFeedbackResult> SubmitFeedbackAsync(SubmitFeedbackInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        // Convert SubmitFeedbackInput (DTO) to SubmitFeedbackRequest (Service DTO)
        var request = new SubmitFeedbackRequest
        {
            FeedbackType = input.FeedbackType,
            Reasons = input.Reasons ?? new(),
            Response = input.Response ?? string.Empty,
            ContactRequested = input.ContactRequested,
            Email = input.Email ?? string.Empty,
            SkippedFeedback = input.SkippedFeedback
        };
        
        var response = await _userFeedbackService.SubmitFeedbackAsync(currentUserId, request);
        
        _logger.LogDebug("[GodGPTUserInfoController][SubmitFeedbackAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return response;
    }

    /// <summary>
    /// Check if user is eligible to submit feedback
    /// </summary>
    [HttpGet("feedback/check")]
    public async Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var response = await _userFeedbackService.CheckFeedbackEligibilityAsync(currentUserId);
        
        _logger.LogDebug("[GodGPTUserInfoController][CheckFeedbackEligibilityAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return response;
    }
}

