using Aevatar.Payment.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Payment.Providers;

/// <summary>
/// Google Play payment provider implementation (via RevenueCat)
/// </summary>
public class GooglePlayProvider : IPaymentProvider
{
    private readonly ILogger<GooglePlayProvider> _logger;
    private readonly GooglePlayOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentPlatform Platform => PaymentPlatform.GooglePlay;

    public GooglePlayProvider(
        ILogger<GooglePlayProvider> logger,
        IOptions<GooglePlayOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("GooglePlay");
    }

    public Task<List<ProductDto>> GetProductsAsync(CancellationToken ct = default)
    {
        // Google Play products are configured in Play Console
        // Return configured products from options
        return Task.FromResult(_options.Products.Select(p => new ProductDto
        {
            ProductId = p.ProductId,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            Currency = p.Currency,
            PlanType = p.PlanType,
            IsActive = true
        }).ToList());
    }

    public async Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest request, 
        CancellationToken ct = default)
    {
        // Google Play subscriptions are created via the app
        // Server-side just needs to verify the transaction
        if (string.IsNullOrEmpty(request.TransactionId) && string.IsNullOrEmpty(request.ReceiptData))
        {
            return new SubscriptionResult
            {
                Success = false,
                ErrorMessage = "TransactionId or PurchaseToken is required for Google Play verification"
            };
        }

        var verification = await VerifyTransactionAsync(new VerificationRequest
        {
            UserId = request.UserId,
            TransactionId = request.TransactionId ?? string.Empty,
            PurchaseToken = request.ReceiptData,
            IsSandbox = request.IsSandbox
        }, ct);

        if (!verification.IsValid)
        {
            return new SubscriptionResult
            {
                Success = false,
                ErrorMessage = verification.ErrorMessage
            };
        }

        return new SubscriptionResult
        {
            Success = true,
            SubscriptionId = verification.OriginalTransactionId ?? verification.TransactionId,
            ExpiresAt = verification.ExpiresDate,
            Status = PaymentStatus.Completed
        };
    }

    public Task<VerificationResult> VerifyTransactionAsync(
        VerificationRequest request, 
        CancellationToken ct = default)
    {
        // For RevenueCat integration, verification happens via webhook
        // This method is mainly for manual verification
        _logger.LogInformation("[GooglePlayProvider] VerifyTransaction called for {TransactionId}",
            request.TransactionId);

        // Return pending verification - actual verification done via webhook
        return Task.FromResult(new VerificationResult
        {
            IsValid = true,
            TransactionId = request.TransactionId,
            ErrorMessage = "Google Play transactions are verified via RevenueCat webhook"
        });
    }

    public Task<WebhookResult> HandleWebhookAsync(
        WebhookRequest request, 
        CancellationToken ct = default)
    {
        try
        {
            // Validate request headers (skip if WebhookAuthToken is empty for testing)
            if (!string.IsNullOrEmpty(_options.WebhookAuthToken))
            {
                if (!ValidateRevenueCatHeaders(request.Headers))
                {
                    return Task.FromResult(new WebhookResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid request headers"
                    });
                }
            }
            else
            {
                _logger.LogWarning("[GooglePlayProvider] Webhook header validation DISABLED (test mode)");
            }

            var webhookEvent = ParseRevenueCatWebhook(request.Payload);
            
            _logger.LogInformation("[GooglePlayProvider] Processing RevenueCat event: {Type}, User: {UserId}",
                webhookEvent.EventType, webhookEvent.AppUserId);

            if (!IsKeyBusinessEvent(webhookEvent))
            {
                return Task.FromResult(new WebhookResult
                {
                    Success = true,
                    ShouldProcess = false,
                    EventType = webhookEvent.EventType
                });
            }

            var result = new WebhookResult
            {
                Success = true,
                EventType = webhookEvent.EventType,
                TransactionId = webhookEvent.TransactionId,
                SubscriptionId = webhookEvent.OriginalTransactionId ?? webhookEvent.TransactionId,
                ShouldProcess = true
            };

            // Extract user ID
            if (TryExtractUserId(webhookEvent, out var userId))
            {
                result.UserId = userId;
            }

            result.NewStatus = MapRevenueCatEventToStatus(webhookEvent.EventType);
            result.ProductId = webhookEvent.ProductId; // For product config lookup
            
            // Determine if this is a renewal - Google Play uses "RENEWAL" event type
            result.IsRenewal = webhookEvent.EventType == "RENEWAL";

            result.VerificationResult = new VerificationResult
            {
                IsValid = true,
                TransactionId = webhookEvent.TransactionId,
                OriginalTransactionId = webhookEvent.OriginalTransactionId,
                ProductId = webhookEvent.ProductId,
                PurchaseDate = webhookEvent.PurchaseDate,
                ExpiresDate = webhookEvent.ExpiresDate,
                AutoRenewing = webhookEvent.AutoRenewing,
                Amount = webhookEvent.Price,
                Currency = webhookEvent.Currency
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayProvider] Webhook processing failed");
            return Task.FromResult(new WebhookResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    public Task<CancellationResult> CancelSubscriptionAsync(
        CancellationRequest request, 
        CancellationToken ct = default)
    {
        // Google Play subscriptions are cancelled via the Play Store app
        return Task.FromResult(new CancellationResult
        {
            Success = false,
            ErrorMessage = "Google Play subscriptions must be cancelled through the Play Store app"
        });
    }

    public Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(
        string subscriptionId, 
        CancellationToken ct = default)
    {
        // For RevenueCat integration, status is maintained via webhooks
        _logger.LogInformation("[GooglePlayProvider] GetSubscriptionStatus called for {SubId}", subscriptionId);
        
        return Task.FromResult(new SubscriptionStatusResult
        {
            IsActive = false,
            Status = PaymentStatus.Pending,
            SubscriptionId = subscriptionId
        });
    }

    #region Private Helpers

    private bool ValidateRevenueCatHeaders(Dictionary<string, string> headers)
    {
        // Validate User-Agent and Authorization
        if (!headers.TryGetValue("User-Agent", out var userAgent))
            return false;
        
        if (!userAgent.Contains("RevenueCat", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!headers.TryGetValue("Authorization", out var auth))
            return false;

        // Validate auth token
        if (!string.IsNullOrEmpty(_options.WebhookAuthToken))
        {
            return auth == $"Bearer {_options.WebhookAuthToken}";
        }

        return !string.IsNullOrEmpty(auth);
    }

    private RevenueCatEvent ParseRevenueCatWebhook(string payload)
    {
        var json = System.Text.Json.JsonDocument.Parse(payload);
        var eventData = json.RootElement.GetProperty("event");

        return new RevenueCatEvent
        {
            EventType = eventData.GetProperty("type").GetString() ?? string.Empty,
            AppUserId = eventData.TryGetProperty("app_user_id", out var appUserId) 
                ? appUserId.GetString() : null,
            OriginalAppUserId = eventData.TryGetProperty("original_app_user_id", out var origUserId) 
                ? origUserId.GetString() : null,
            ProductId = eventData.TryGetProperty("product_id", out var prodId) 
                ? prodId.GetString() : null,
            TransactionId = eventData.TryGetProperty("transaction_id", out var txId) 
                ? txId.GetString() : null,
            OriginalTransactionId = eventData.TryGetProperty("original_transaction_id", out var origTxId) 
                ? origTxId.GetString() : null,
            Price = eventData.TryGetProperty("price_in_purchased_currency", out var price) 
                ? (decimal?)price.GetDouble() : null,
            Currency = eventData.TryGetProperty("currency", out var currency) 
                ? currency.GetString() : null,
            PurchaseDate = eventData.TryGetProperty("purchased_at_ms", out var purchMs) 
                ? DateTimeOffset.FromUnixTimeMilliseconds(purchMs.GetInt64()).DateTime 
                : null,
            ExpiresDate = eventData.TryGetProperty("expiration_at_ms", out var expMs) && expMs.ValueKind != System.Text.Json.JsonValueKind.Null
                ? DateTimeOffset.FromUnixTimeMilliseconds(expMs.GetInt64()).DateTime 
                : null,
            CancelReason = eventData.TryGetProperty("cancel_reason", out var cancelReason) 
                ? cancelReason.GetString() : null,
            PeriodType = eventData.TryGetProperty("period_type", out var periodType) 
                ? periodType.GetString() : null,
            Aliases = eventData.TryGetProperty("aliases", out var aliases)
                ? aliases.EnumerateArray().Select(a => a.GetString() ?? string.Empty).ToList()
                : new List<string>()
        };
    }

    private static bool IsKeyBusinessEvent(RevenueCatEvent evt)
    {
        var isCoreEvent = evt.EventType switch
        {
            "INITIAL_PURCHASE" => true,
            "RENEWAL" => true,
            "CANCELLATION" => true,
            "EXPIRATION" => true,
            "UNCANCELLATION" => true,
            "PRODUCT_CHANGE" => true,
            "BILLING_ISSUE" => true,
            _ => false
        };

        if (!isCoreEvent) return false;

        // For status change events, always process
        if (evt.EventType is "CANCELLATION" or "EXPIRATION" or "UNCANCELLATION" or "BILLING_ISSUE")
            return true;

        // For purchase/renewal/product_change, require non-zero price
        return evt.Price > 0;
    }

    private static bool TryExtractUserId(RevenueCatEvent evt, out Guid userId)
    {
        userId = default;

        if (!string.IsNullOrEmpty(evt.AppUserId) && Guid.TryParse(evt.AppUserId, out userId))
            return true;

        if (!string.IsNullOrEmpty(evt.OriginalAppUserId) && Guid.TryParse(evt.OriginalAppUserId, out userId))
            return true;

        foreach (var alias in evt.Aliases)
        {
            if (!string.IsNullOrEmpty(alias) && Guid.TryParse(alias, out userId))
                return true;
        }

        return false;
    }

    private static PaymentStatus MapRevenueCatEventToStatus(string eventType)
    {
        return eventType switch
        {
            "INITIAL_PURCHASE" => PaymentStatus.Completed,
            "RENEWAL" => PaymentStatus.Completed,
            "CANCELLATION" => PaymentStatus.Cancelled,
            "EXPIRATION" => PaymentStatus.Expired,
            "REFUND" => PaymentStatus.Refunded,
            "UNCANCELLATION" => PaymentStatus.Completed, // User re-enabled auto-renewal
            "PRODUCT_CHANGE" => PaymentStatus.Completed, // User changed subscription product
            "BILLING_ISSUE" => PaymentStatus.Failed,     // Payment method issue
            _ => PaymentStatus.Pending
        };
    }

    #endregion

    #region Internal Types

    private class RevenueCatEvent
    {
        public string EventType { get; set; } = string.Empty;
        public string? AppUserId { get; set; }
        public string? OriginalAppUserId { get; set; }
        public string? ProductId { get; set; }
        public string? TransactionId { get; set; }
        public string? OriginalTransactionId { get; set; }
        public decimal? Price { get; set; }
        public string? Currency { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public DateTime? ExpiresDate { get; set; }
        public string? CancelReason { get; set; }
        public string? PeriodType { get; set; }
        public List<string> Aliases { get; set; } = new();
        public bool AutoRenewing => string.IsNullOrEmpty(CancelReason) && PeriodType == "NORMAL";
    }

    #endregion
}

/// <summary>
/// Google Play configuration options (via RevenueCat)
/// </summary>
public class GooglePlayOptions
{
    public const string SectionName = "GooglePlay";
    
    public string WebhookAuthToken { get; set; } = string.Empty;
    public string RevenueCatApiKey { get; set; } = string.Empty;
    public List<GoogleProductConfig> Products { get; set; } = new();
}

public class GoogleProductConfig
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public PlanType PlanType { get; set; }
    public bool IsUltimate { get; set; }
}

