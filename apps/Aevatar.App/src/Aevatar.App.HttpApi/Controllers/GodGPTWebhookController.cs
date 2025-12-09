using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Security;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.Webhook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Orleans;
using Stripe;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for handling payment webhook callbacks from various providers
/// </summary>
[RemoteService]
[Route("api/webhooks")]
[AllowAnonymous]
[ApiController]
public class GodGPTWebhookController : AevatarController
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<GodGPTWebhookController> _logger;
    private readonly GooglePaySecurityValidator _securityValidator;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    
    private const string AppleNotificationProcessorGrainId = "AppleNotificationProcessorGrainId_1";
    private const string StripeEventProcessingGrainId = "StripeEventProcessingGrainId_1";

    public GodGPTWebhookController(
        IClusterClient clusterClient,
        ILogger<GodGPTWebhookController> logger,
        GooglePaySecurityValidator securityValidator,
        IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _securityValidator = securityValidator;
        _stripeOptions = stripeOptions;
    }

    #region Google Play (RevenueCat) Webhook

    /// <summary>
    /// Handle RevenueCat webhook for Google Play payments
    /// </summary>
    [HttpPost("godgpt-googleplay-payment")]
    public async Task<IActionResult> HandleGooglePlayWebhookAsync()
    {
        try
        {
            var userAgent = Request.Headers["User-Agent"].ToString();
            var contentType = Request.Headers["Content-Type"].ToString();
            var authorizationHeader = Request.Headers["Authorization"].ToString();
            
            var maskedAuth = string.IsNullOrEmpty(authorizationHeader) ? "None" : 
                authorizationHeader.Length > 4 ? authorizationHeader.Substring(0, 4) + "***" : "***";
            
            _logger.LogDebug("[GooglePlayWebhook] Received: Method={Method}, Path={Path}", 
                Request.Method, Request.Path);
            _logger.LogDebug("[GooglePlayWebhook] Headers - UserAgent: {UserAgent}, ContentType: {ContentType}, Auth: {Auth}", 
                userAgent, contentType, maskedAuth);

            if (!_securityValidator.ValidateRequestHeaders(userAgent, contentType, authorizationHeader))
            {
                _logger.LogWarning("[GooglePlayWebhook] Request failed header validation");
                return Ok(new { success = false, message = "Invalid request headers" });
            }

            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();
            _logger.LogInformation("[GooglePlayWebhook] Received body: {Body}", json);
            
            return Ok(await ProcessRevenueCatWebhookAsync(json));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayWebhook] Error processing webhook");
            return Ok(new { success = false, error = "Internal server error" });
        }
    }
    
    private async Task<object> ProcessRevenueCatWebhookAsync(string json)
    {
        try
        {
            var webhookEvent = JsonConvert.DeserializeObject<RevenueCatWebhookEvent>(json);
            if (webhookEvent?.Event == null)
            {
                _logger.LogWarning("[GooglePlayWebhook] Invalid webhook event format");
                return new { success = false, message = "Invalid webhook event format" };
            }

            var eventData = webhookEvent.Event;
            _logger.LogInformation("[GooglePlayWebhook] Event: {Type}, UserId: {UserId}, ProductId: {ProductId}, Price: {Price}",
                eventData.Type, eventData.AppUserId, eventData.ProductId, eventData.PriceInPurchasedCurrency);

            if (!IsKeyRevenueCatBusinessEvent(eventData.Type, eventData.PriceInPurchasedCurrency))
            {
                _logger.LogInformation("[GooglePlayWebhook] Filtering event: Type={Type}, Price={Price}", 
                    eventData.Type, eventData.PriceInPurchasedCurrency);
                return new { success = true, message = "Event received but filtered by type or amount" };
            }

            if (!TryExtractUserIdFromRevenueCat(eventData, out var userId))
            {
                _logger.LogWarning("[GooglePlayWebhook] Could not extract user ID from event");
                return new { success = true, message = "Event received but no associated user found" };
            }

            var userBillingGAgent = _clusterClient.GetGrain<IUserBillingGAgent>(userId);
            var verificationResult = CreateRevenueCatVerificationResult(eventData);
            var result = await userBillingGAgent.ProcessRevenueCatWebhookEventAsync(userId, eventData.Type, verificationResult);
            
            if (!result)
            {
                _logger.LogWarning("[GooglePlayWebhook] Failed to process notification for user {UserId}", userId);
                return new { success = false, message = "Failed to process notification" };
            }
            
            _logger.LogInformation("[GooglePlayWebhook] Successfully processed for user {UserId}", userId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayWebhook] Error processing RevenueCat webhook");
            return new { success = false, error = "Internal server error" };
        }
    }
    
    private bool IsKeyRevenueCatBusinessEvent(string eventType, double? priceInPurchasedCurrency)
    {
        bool isCoreBusinessEvent = eventType switch
        {
            RevenueCatWebhookEventTypes.INITIAL_PURCHASE => true,
            RevenueCatWebhookEventTypes.RENEWAL => true,
            RevenueCatWebhookEventTypes.CANCELLATION => true,
            RevenueCatWebhookEventTypes.EXPIRATION => true,
            _ => false
        };
        
        if (!isCoreBusinessEvent) return false;
        
        if (eventType == RevenueCatWebhookEventTypes.CANCELLATION || 
            eventType == RevenueCatWebhookEventTypes.EXPIRATION)
        {
            return true;
        }
        
        return priceInPurchasedCurrency.HasValue && priceInPurchasedCurrency.Value > 0;
    }
    
    private bool TryExtractUserIdFromRevenueCat(RevenueCatEvent eventData, out Guid userId)
    {
        userId = default;
        
        if (!string.IsNullOrEmpty(eventData.AppUserId) && Guid.TryParse(eventData.AppUserId, out userId))
            return true;
        
        if (!string.IsNullOrEmpty(eventData.OriginalAppUserId) && Guid.TryParse(eventData.OriginalAppUserId, out userId))
            return true;
        
        foreach (var alias in eventData.Aliases ?? new List<string>())
        {
            if (!string.IsNullOrEmpty(alias) && Guid.TryParse(alias, out userId))
                return true;
        }
        
        return false;
    }
    
    private PaymentVerificationResultDto CreateRevenueCatVerificationResult(RevenueCatEvent eventData)
    {
        int paymentState = 1;
        bool autoRenewing = string.IsNullOrEmpty(eventData.CancelReason) && eventData.PeriodType == "NORMAL";

        _logger.LogInformation("[GooglePlayWebhook] Creating verification: ProductId={ProductId}, TransactionId={TransactionId}, Price={Price} {Currency}", 
            eventData.ProductId, eventData.TransactionId, eventData.PriceInPurchasedCurrency, eventData.Currency);

        return new PaymentVerificationResultDto
        {
            IsValid = true,
            TransactionId = eventData.TransactionId ?? eventData.OriginalTransactionId,
            ProductId = eventData.ProductId,
            SubscriptionStartDate = null,
            SubscriptionEndDate = null,
            Platform = PaymentPlatform.GooglePlay,
            PurchaseToken = eventData.OriginalTransactionId ?? eventData.TransactionId,
            OriginalTransactionId = eventData.OriginalTransactionId ?? eventData.TransactionId,
            Message = $"RevenueCat webhook. CancelReason: {eventData.CancelReason}, Price: {eventData.PriceInPurchasedCurrency} {eventData.Currency}",
            PaymentState = paymentState,
            AutoRenewing = autoRenewing,
            PurchaseTimeMillis = eventData.PurchasedAtMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PriceInPurchasedCurrency = eventData.PriceInPurchasedCurrency,
            OrderId = eventData.OriginalTransactionId ?? eventData.TransactionId ?? Guid.NewGuid().ToString()
        };
    }

    #endregion

    #region Apple App Store Webhook

    /// <summary>
    /// Handle Apple App Store Server Notifications
    /// </summary>
    [HttpPost("godgpt-appstore-payment")]
    public async Task<IActionResult> HandleAppStoreWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[AppStoreWebhook] Received: Method={Method}, Path={Path}, Query={Query}",
                Request.Method, Request.Path, Request.QueryString);

            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();
            
            var appleEventProcessingGrain = _clusterClient.GetGrain<IAppleEventProcessingGrain>(AppleNotificationProcessorGrainId);
            var (userId, notificationType, subtype) = await appleEventProcessingGrain.ParseEventAndGetUserIdAsync(json);
            
            _logger.LogInformation("[AppStoreWebhook] userId:{UserId}, type:{Type}, subtype:{Subtype}",
                userId, notificationType, subtype);
            
            if (userId == default)
            {
                _logger.LogWarning("[AppStoreWebhook] Could not determine user ID from notification");
                return Ok(new { success = true, message = "Notification received but no associated user found" });
            }
            
            if (!IsAllowedAppleNotificationType(notificationType, subtype))
            {
                _logger.LogInformation("[AppStoreWebhook] Filtered type={Type}, subtype={Subtype}", notificationType, subtype);
                return Ok(new { success = true, message = "Notification received but filter by type" });
            }
            
            var userBillingGAgent = _clusterClient.GetGrain<IUserBillingGAgent>(userId);
            var result = await userBillingGAgent.HandleAppStoreNotificationAsync(userId, json);
            
            if (!result)
            {
                _logger.LogWarning("[AppStoreWebhook] Failed to process notification for user {UserId}", userId);
                return Ok(new { success = false, message = "Failed to process notification" });
            }
            
            _logger.LogInformation("[AppStoreWebhook] Successfully processed for user {UserId}", userId);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppStoreWebhook] Error processing webhook");
            return Ok(new { success = false, error = "Internal server error" });
        }
    }
    
    private bool IsAllowedAppleNotificationType(string notificationType, string subtype)
    {
        return notificationType == AppStoreNotificationType.SUBSCRIBED.ToString()
            || notificationType == AppStoreNotificationType.DID_RENEW.ToString()
            || (notificationType == AppStoreNotificationType.DID_CHANGE_RENEWAL_STATUS.ToString() 
                && subtype == AppStoreNotificationSubtype.AUTO_RENEW_DISABLED.ToString())
            || notificationType == AppStoreNotificationType.EXPIRED.ToString()
            || notificationType == AppStoreNotificationType.GRACE_PERIOD_EXPIRED.ToString()
            || notificationType == AppStoreNotificationType.REVOKE.ToString()
            || notificationType == AppStoreNotificationType.DID_CHANGE_RENEWAL_PREF.ToString()
            || notificationType == AppStoreNotificationType.REFUND.ToString();
    }

    #endregion

    #region Stripe Webhook

    /// <summary>
    /// Handle Stripe payment webhook events
    /// </summary>
    [HttpPost("godgpt-stripe-payment")]
    public async Task<IActionResult> HandleStripeWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[StripeWebhook] Received: Method={Method}, Path={Path}, Query={Query}",
                Request.Method, Request.Path, Request.QueryString);

            var signature = Request.Headers["Stripe-Signature"].ToString();
            _logger.LogDebug("[StripeWebhook] Signature={Signature}", signature);
            
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();
            _logger.LogDebug("[StripeWebhook] Body: {Body}", json);

            string internalUserId = null;
            try
            {
                var stripeEventGrain = _clusterClient.GetGrain<IStripeEventProcessingGrain>(StripeEventProcessingGrainId);
                internalUserId = await stripeEventGrain.ParseEventAndGetUserIdAsync(json);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[StripeWebhook] Error validating webhook: {Message}", e.Message);
                return Ok();
            }

            if (!string.IsNullOrWhiteSpace(internalUserId) && Guid.TryParse(internalUserId, out var userId))
            {
                var result = await ProcessStripeWebhookEventAsync(userId, json, signature);
                _logger.LogInformation("[StripeWebhook] result={Result}", result);
            }
            else
            {
                _logger.LogWarning("[StripeWebhook] User not found: {UserId}", internalUserId);
            }
            
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StripeWebhook] Error processing webhook");
            throw;
        }
    }
    
    private async Task<bool> ProcessStripeWebhookEventAsync(Guid internalUserId, string json, StringValues stripeSignature)
    {
        var userBillingGrain = _clusterClient.GetGrain<IUserBillingGAgent>(internalUserId);
        return await userBillingGrain.HandleStripeWebhookEventAsync(json, stripeSignature);
    }

    #endregion
}

