using System;
using System.Threading.Tasks;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Aevatar.App.Controllers;

/// <summary>
/// User subscription status API.
/// </summary>
[RemoteService]
[Route("api/subscription")]
[Authorize]
public class UserSubscriptionController : AevatarController
{
    private readonly IUserSubscriptionService _userSubscriptionService;

    public UserSubscriptionController(IUserSubscriptionService userSubscriptionService)
    {
        _userSubscriptionService = userSubscriptionService;
    }

    /// <summary>
    /// Get current user's subscription information.
    /// </summary>
    /// <returns>User subscription information</returns>
    [HttpGet("current")]
    public async Task<UserSubscriptionDto> GetCurrentSubscriptionAsync()
    {
        var userId = CurrentUser.Id ?? throw new UnauthorizedAccessException("User not authenticated");
        return await _userSubscriptionService.GetCurrentSubscriptionAsync(userId);
    }
}
