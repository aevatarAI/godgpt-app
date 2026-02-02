using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.App.Application.Contracts.Services.Push;
using Aevatar.Application.Grains.Agents.UserDevice;
using Aevatar.Dtos.Push;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Application.Services.Push;

/// <summary>
/// Service for sending push notifications.
/// </summary>
public class PushNotificationService : IPushNotificationService
{
    private const string UserDeviceAgentType = "Aevatar.Application.Grains.Agents.UserDevice.UserDeviceGAgent";
    private const int PageSize = 500;
    private const int MaxPages = 20; // Safety limit: 10,000 users max

    private const string PushType = "2";
    
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IStateIndexService? _stateIndexService;
    private readonly IFirebaseMessagingClient _firebaseClient;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        IGAgentActorFactory actorFactory,
        IFirebaseMessagingClient firebaseClient,
        ILogger<PushNotificationService> logger,
        IStateIndexService? stateIndexService = null)
    {
        _actorFactory = actorFactory;
        _firebaseClient = firebaseClient;
        _logger = logger;
        _stateIndexService = stateIndexService;
    }

    /// <inheritdoc />
    public async Task<PushResult> SendByTimezoneAsync(SendPushByTimezoneInput input, CancellationToken ct = default)
    {
        if (_stateIndexService == null)
        {
            _logger.LogWarning("[PushNotificationService][SendByTimezone] StateIndexService not available");
            return new PushResult { Error = "State index service not available" };
        }
        
        _logger.LogInformation(
            "[PushNotificationService][SendByTimezone] TimeZone: {TimeZone}, Title: {Title}",
            input.TimeZoneId, input.Title);
        
        var result = new PushResult();
        int pageIndex = 0;

        var data = new Dictionary<string, string>();
        data["type"] = PushType;
        
        var message = new PushMessage
        {
            Title = input.Title,
            Body = input.Body,
            Data = data
        };
        
        try
        {
            while (pageIndex < MaxPages)
            {
                var query = new StateQuery
                {
                    AgentType = UserDeviceAgentType,
                    QueryString = BuildTimezoneQuery(input.TimeZoneId),
                    PageIndex = pageIndex,
                    PageSize = PageSize,
                    SortFields = new List<string> { "userId:asc" }
                };
                
                var queryResult = await _stateIndexService.QueryAsync(query, ct);
                
                if (queryResult.Items.Count == 0)
                {
                    break;
                }
                
                result.TotalTargeted += queryResult.Items.Count;
                
                // Extract push tokens
                var tokens = queryResult.Items
                    .Select(x => x.Data.TryGetValue("pushToken", out var token) ? token?.ToString() : null)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Cast<string>()
                    .ToList();
                
                if (tokens.Count > 0)
                {
                    var batchResult = await _firebaseClient.SendBatchAsync(tokens, message, ct);
                    
                    result.SuccessCount += batchResult.SuccessCount;
                    result.FailureCount += batchResult.FailureCount;
                    
                    // Mark invalid tokens
                    await MarkInvalidTokensAsync(queryResult.Items, batchResult.FailedTokens);
                }
                
                // Check if we've reached the end
                if (queryResult.Items.Count < PageSize)
                {
                    break;
                }
                
                pageIndex++;
            }
            
            _logger.LogInformation(
                "[PushNotificationService][SendByTimezone] Completed - TimeZone: {TimeZone}, Targeted: {Targeted}, Success: {Success}, Failed: {Failed}",
                input.TimeZoneId, result.TotalTargeted, result.SuccessCount, result.FailureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PushNotificationService][SendByTimezone] Failed for TimeZone: {TimeZone}", input.TimeZoneId);
            result.Error = ex.Message;
        }
        
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> SendToUserAsync(Guid userId, string title, string body, CancellationToken ct = default)
    {
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<UserDeviceGAgent>(userId.ToString());
            var agent = actor.As<IUserDeviceGAgent>();
            var pushTarget = await agent.GetPushTargetAsync();
            
            if (pushTarget == null)
            {
                _logger.LogDebug("[PushNotificationService][SendToUser] No push target for UserId: {UserId}", userId);
                return false;
            }
            
            var message = new PushMessage
            {
                Title = title,
                Body = body
            };
            
            var result = await _firebaseClient.SendAsync(pushTarget.PushToken, message, ct);
            
            if (result.TokenInvalid)
            {
                await agent.MarkTokenInvalidAsync();
            }
            
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PushNotificationService][SendToUser] Failed for UserId: {UserId}", userId);
            return false;
        }
    }

    private static string BuildTimezoneQuery(string timezone)
    {
        // Escape special characters in timezone ID
        var escapedTimezone = timezone.Replace(":", "\\:").Replace("/", "\\/");
        return $"timeZoneId.keyword:\"{escapedTimezone}\" AND pushEnabled:true AND tokenInvalid:false";
    }

    private async Task MarkInvalidTokensAsync(
        List<StateQueryResult> items,
        List<(string Token, string? Error, bool TokenInvalid)> failedTokens)
    {
        var invalidTokens = failedTokens
            .Where(f => f.TokenInvalid)
            .Select(f => f.Token)
            .ToHashSet();
        
        if (invalidTokens.Count == 0)
        {
            return;
        }
        
        foreach (var item in items)
        {
            if (item.Data.TryGetValue("pushToken", out var tokenObj) && 
                tokenObj?.ToString() is string token &&
                invalidTokens.Contains(token))
            {
                try
                {
                    if (Guid.TryParse(item.AgentId, out var userId))
                    {
                        var actor = await _actorFactory.CreateGAgentActorAsync<UserDeviceGAgent>(userId.ToString());
                        var agent = actor.As<IUserDeviceGAgent>();
                        await agent.MarkTokenInvalidAsync();
                        
                        _logger.LogDebug("[PushNotificationService] Marked token invalid for UserId: {UserId}", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PushNotificationService] Failed to mark token invalid for AgentId: {AgentId}", item.AgentId);
                }
            }
        }
    }
}
