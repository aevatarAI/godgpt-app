using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.Payment.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Asp.Versioning;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Webhook controller for handling payment callbacks from various providers.
/// Maintains backward compatibility with existing webhook URLs.
/// Internally delegates to PaymentService.
/// </summary>
[RemoteService]
[ControllerName("GodGPTWebhook")]
[Route("api/webhooks")]
[AllowAnonymous]
public class GodGPTWebhookController : AevatarController
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<GodGPTWebhookController> _logger;

    public GodGPTWebhookController(
        IPaymentService paymentService,
        ILogger<GodGPTWebhookController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    #region Google Play (RevenueCat) Webhook

    /// <summary>
    /// Handle RevenueCat webhook for Google Play payments.
    /// Legacy endpoint: /api/webhooks/godgpt-googleplay-payment
    /// </summary>
    [HttpPost("godgpt-googleplay-payment")]
    public async Task<IActionResult> HandleGooglePlayWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[GodGPTWebhook] Google Play webhook received");

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = Request.Headers["User-Agent"].ToString(),
                ["Content-Type"] = Request.Headers["Content-Type"].ToString(),
                ["Authorization"] = Request.Headers["Authorization"].ToString()
            };

            var request = new WebhookRequest
            {
                Payload = payload,
                Headers = headers
            };

            var result = await _paymentService.HandleWebhookAsync(PaymentPlatform.GooglePlay, request);

            _logger.LogInformation("[GodGPTWebhook] Google Play result: Success={Success}, Event={Event}, UserId={UserId}",
                result.Success, result.EventType, result.UserId);

            if (!result.Success)
            {
                return Ok(new { success = false, message = result.ErrorMessage ?? "Failed to process notification" });
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTWebhook] Google Play webhook error");
            return Ok(new { success = false, error = "Internal server error" });
        }
    }

    #endregion

    #region Apple App Store Webhook

    /// <summary>
    /// Handle Apple App Store Server Notifications.
    /// Legacy endpoint: /api/webhooks/godgpt-appstore-payment
    /// </summary>
    [HttpPost("godgpt-appstore-payment")]
    public async Task<IActionResult> HandleAppStoreWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[GodGPTWebhook] App Store webhook received");

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            var request = new WebhookRequest
            {
                Payload = payload
            };

            var result = await _paymentService.HandleWebhookAsync(PaymentPlatform.AppStore, request);

            _logger.LogInformation("[GodGPTWebhook] App Store result: Success={Success}, Event={Event}, UserId={UserId}",
                result.Success, result.EventType, result.UserId);

            if (!result.Success)
            {
                return Ok(new { success = false, message = result.ErrorMessage ?? "Failed to process notification" });
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTWebhook] App Store webhook error");
            return Ok(new { success = false, error = "Internal server error" });
        }
    }

    #endregion

    #region Stripe Webhook

    /// <summary>
    /// Handle Stripe payment webhook events.
    /// Legacy endpoint: /api/webhooks/godgpt-stripe-payment
    /// </summary>
    [HttpPost("godgpt-stripe-payment")]
    public async Task<IActionResult> HandleStripeWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[GodGPTWebhook] Stripe webhook received");

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"].ToString();

            var request = new WebhookRequest
            {
                Payload = payload,
                Signature = signature
            };

            var result = await _paymentService.HandleWebhookAsync(PaymentPlatform.Stripe, request);

            _logger.LogInformation("[GodGPTWebhook] Stripe result: Success={Success}, Event={Event}, UserId={UserId}",
                result.Success, result.EventType, result.UserId);

            // Stripe requires 200 OK to acknowledge receipt
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTWebhook] Stripe webhook error");
            throw;
        }
    }

    #endregion
}
