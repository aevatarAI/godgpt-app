using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Options;
using Microsoft.AspNetCore.Authorization;
using StripeOptions = Aevatar.Payment.Providers.StripeOptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Users;

namespace Aevatar.Payment.Controllers;

/// <summary>
/// Payment API Controller (New API)
/// Note: For backward compatibility, use GodGPTPaymentController (/api/godgpt/payment)
/// </summary>
[RemoteService]
[Route("api/payment")]
[Authorize]
public class PaymentController : AbpControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly StripeOptions _stripeOptions;

    public PaymentController(
        IPaymentService paymentService,
        ILogger<PaymentController> logger,
        ICurrentUser currentUser,
        IOptions<StripeOptions> stripeOptions)
    {
        _paymentService = paymentService;
        _logger = logger;
        _currentUser = currentUser;
        _stripeOptions = stripeOptions.Value;
    }

    /// <summary>
    /// Get available products for a payment platform
    /// </summary>
    [HttpGet("products/{platform}")]
    public async Task<ActionResult<List<ProductDto>>> GetProductsAsync(PaymentPlatform platform)
    {
        var products = await _paymentService.GetProductsAsync(platform);
        return Ok(products);
    }

    /// <summary>
    /// Create a subscription checkout session
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<ActionResult<SubscriptionResult>> CreateSubscriptionAsync(
        [FromBody] CreateSubscriptionRequest request)
    {
        var userId = _currentUser.Id ?? throw new UnauthorizedAccessException();
        
        _logger.LogInformation("[PaymentController] Creating subscription for user {UserId} on {Platform}",
            userId, request.Platform);

        var result = await _paymentService.CreateSubscriptionAsync(userId, request.Platform, new SubscriptionRequest
        {
            ProductId = request.ProductId,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            TransactionId = request.TransactionId,
            ReceiptData = request.ReceiptData,
            IsSandbox = request.IsSandbox,
            Mode = request.Mode,
            UiMode = request.UiMode,
            CouponCode = request.CouponCode,
            TrialDays = request.TrialDays
        });

        return Ok(result);
    }

    /// <summary>
    /// Verify a transaction
    /// </summary>
    [HttpPost("verify")]
    public async Task<ActionResult<VerificationResult>> VerifyTransactionAsync(
        [FromBody] VerifyTransactionRequest request)
    {
        var userId = _currentUser.Id ?? throw new UnauthorizedAccessException();
        
        _logger.LogInformation("[PaymentController] Verifying transaction for user {UserId} on {Platform}",
            userId, request.Platform);

        var result = await _paymentService.VerifyTransactionAsync(userId, request.Platform, new VerificationRequest
        {
            TransactionId = request.TransactionId,
            ReceiptData = request.ReceiptData,
            PurchaseToken = request.PurchaseToken,
            IsSandbox = request.IsSandbox
        });

        return Ok(result);
    }

    /// <summary>
    /// Get subscription status
    /// </summary>
    [HttpGet("subscription-status")]
    public async Task<ActionResult<UserSubscriptionStatus>> GetSubscriptionStatusAsync()
    {
        var userId = _currentUser.Id ?? throw new UnauthorizedAccessException();
        var status = await _paymentService.GetUserSubscriptionStatusAsync(userId);
        return Ok(status);
    }

    /// <summary>
    /// Get payment history
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<PaymentHistoryItem>>> GetPaymentHistoryAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var userId = _currentUser.Id ?? throw new UnauthorizedAccessException();
        var history = await _paymentService.GetPaymentHistoryAsync(userId, page, pageSize);
        return Ok(history);
    }

    /// <summary>
    /// Get Stripe customer info with ephemeral key (for mobile SDK)
    /// </summary>
    [HttpGet("customer")]
    public async Task<ActionResult<CustomerSessionResult>> GetCustomerAsync()
    {
        var userId = _currentUser.Id ?? throw new UnauthorizedAccessException();
        
        _logger.LogInformation("[PaymentController] Getting customer for user {UserId}", userId);

        var result = await _paymentService.GetStripeCustomerAsync(userId);
        return Ok(result);
    }

    /// <summary>
    /// Get Stripe publishable key
    /// </summary>
    [HttpGet("keys")]
    public ActionResult<object> GetKeys()
    {
        return Ok(new { publishableKey = _stripeOptions.PublishableKey ?? "" });
    }

    /// <summary>
    /// Cancel a subscription
    /// </summary>
    [HttpPost("cancel")]
    public async Task<ActionResult<CancellationResult>> CancelSubscriptionAsync(
        [FromBody] CancelSubscriptionRequest request)
    {
        var userId = _currentUser.Id ?? throw new UnauthorizedAccessException();
        
        _logger.LogInformation("[PaymentController] Cancelling subscription {SubId} for user {UserId}",
            request.SubscriptionId, userId);

        var result = await _paymentService.CancelSubscriptionAsync(userId, request.Platform, new CancellationRequest
        {
            SubscriptionId = request.SubscriptionId,
            Reason = request.Reason,
            Immediate = request.Immediate
        });

        return Ok(result);
    }
}

#region Request DTOs

public class CreateSubscriptionRequest
{
    public PaymentPlatform Platform { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
    public string? TransactionId { get; set; }
    public string? ReceiptData { get; set; }
    public bool IsSandbox { get; set; }
    
    // Stripe-specific
    public string? Mode { get; set; }
    public string? UiMode { get; set; }
    public string? CouponCode { get; set; }
    public int TrialDays { get; set; }
}

public class VerifyTransactionRequest
{
    public PaymentPlatform Platform { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string? ReceiptData { get; set; }
    public string? PurchaseToken { get; set; }
    public bool IsSandbox { get; set; }
}

public class CancelSubscriptionRequest
{
    public PaymentPlatform Platform { get; set; }
    public string SubscriptionId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool Immediate { get; set; }
}

#endregion
