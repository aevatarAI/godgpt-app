using Aevatar.Payment.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.Payment.Controllers;

/// <summary>
/// Webhook controller for handling payment platform callbacks
/// </summary>
[RemoteService]
[Route("api/payment/webhooks")]
[AllowAnonymous]
public class PaymentWebhookController : AbpControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPaymentService paymentService,
        ILogger<PaymentWebhookController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Handle Stripe webhook
    /// </summary>
    [HttpPost("stripe")]
    public async Task<IActionResult> HandleStripeWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[PaymentWebhook] Stripe webhook received");

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"].ToString();

            var request = new WebhookRequest
            {
                Payload = payload,
                Signature = signature
            };

            var result = await _paymentService.HandleWebhookAsync(PaymentPlatform.Stripe, request);

            _logger.LogInformation("[PaymentWebhook] Stripe result: Success={Success}, Event={Event}, UserId={UserId}",
                result.Success, result.EventType, result.UserId);

            return Ok(new { success = result.Success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentWebhook] Stripe webhook error");
            return Ok(new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Handle Apple App Store webhook
    /// </summary>
    [HttpPost("appstore")]
    public async Task<IActionResult> HandleAppStoreWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[PaymentWebhook] App Store webhook received");

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            var request = new WebhookRequest
            {
                Payload = payload
            };

            var result = await _paymentService.HandleWebhookAsync(PaymentPlatform.AppStore, request);

            _logger.LogInformation("[PaymentWebhook] App Store result: Success={Success}, Event={Event}, UserId={UserId}",
                result.Success, result.EventType, result.UserId);

            return Ok(new { success = result.Success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentWebhook] App Store webhook error");
            return Ok(new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Handle Google Play webhook (via RevenueCat)
    /// </summary>
    [HttpPost("googleplay")]
    public async Task<IActionResult> HandleGooglePlayWebhookAsync()
    {
        try
        {
            _logger.LogDebug("[PaymentWebhook] Google Play webhook received");

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            // Extract relevant headers for RevenueCat validation
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

            _logger.LogInformation("[PaymentWebhook] Google Play result: Success={Success}, Event={Event}, UserId={UserId}",
                result.Success, result.EventType, result.UserId);

            return Ok(new { success = result.Success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentWebhook] Google Play webhook error");
            return Ok(new { success = false, error = "Internal server error" });
        }
    }
}

