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
        // Return configured products from options with originalPlanType metadata
        return Task.FromResult(_options.Products.Select(p => new ProductDto
        {
            ProductId = p.ProductId,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            Currency = p.Currency,
            PlanType = p.IsUltimate ? PlanType.Premium : PlanType.Basic,
            BillingCycle = MapPlanTypeToBillingCycle(p.PlanType),
            IsActive = true,
            Metadata = new Dictionary<string, string>
            {
                ["originalPlanType"] = p.PlanType.ToString(),
                ["isUltimate"] = p.IsUltimate.ToString().ToLower()
            }
        }).ToList());
    }
    
    /// <summary>
    /// Maps legacy PlanType (1=Day, 2=Month, 3=Year, 4=Week) to BillingCycle
    /// </summary>
    private static BillingCycle MapPlanTypeToBillingCycle(int planType) => planType switch
    {
        1 => BillingCycle.Daily,
        2 => BillingCycle.Monthly,
        3 => BillingCycle.Yearly,
        4 => BillingCycle.Weekly,
        _ => BillingCycle.Monthly
    };

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

    public async Task<VerificationResult> VerifyTransactionAsync(
        VerificationRequest request, 
        CancellationToken ct = default)
    {
        _logger.LogInformation("[GooglePlayProvider] VerifyTransaction called for {TransactionId}, UserId: {UserId}",
            request.TransactionId, request.UserId);

        // Validate RevenueCat configuration
        if (string.IsNullOrEmpty(_options.RevenueCatApiKey))
        {
            _logger.LogWarning("[GooglePlayProvider] RevenueCat API key not configured, falling back to webhook verification");
            return new VerificationResult
            {
                IsValid = true,
                TransactionId = request.TransactionId,
                ErrorMessage = "RevenueCat API key not configured - verification via webhook"
            };
        }

        if (request.UserId == Guid.Empty)
        {
            _logger.LogWarning("[GooglePlayProvider] UserId required for RevenueCat verification");
            return new VerificationResult
            {
                IsValid = false,
                ErrorMessage = "UserId is required for Google Play verification"
            };
        }

        try
        {
            // Query RevenueCat subscriber API
            var requestUrl = $"{_options.RevenueCatBaseUrl}/subscribers/{request.UserId}";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.RevenueCatApiKey}");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            
            var response = await _httpClient.GetAsync(requestUrl, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[GooglePlayProvider] RevenueCat API error: {StatusCode}, Content: {Content}",
                    response.StatusCode, errorContent);
                return new VerificationResult
                {
                    IsValid = false,
                    ErrorMessage = $"RevenueCat API returned {response.StatusCode}"
                };
            }
            
            var content = await response.Content.ReadAsStringAsync(ct);
            var revenueCatData = ParseRevenueCatSubscriber(content, request.TransactionId);
            
            if (revenueCatData == null)
            {
                _logger.LogWarning("[GooglePlayProvider] Transaction {TransactionId} not found in RevenueCat for user {UserId}",
                    request.TransactionId, request.UserId);
                return new VerificationResult
                {
                    IsValid = false,
                    ErrorMessage = "Transaction not found in RevenueCat"
                };
            }
            
            _logger.LogInformation("[GooglePlayProvider] Transaction verified via RevenueCat: {TransactionId}, ProductId: {ProductId}",
                request.TransactionId, revenueCatData.ProductId);
            
            return new VerificationResult
            {
                IsValid = true,
                TransactionId = request.TransactionId,
                OriginalTransactionId = revenueCatData.OriginalTransactionId,
                ProductId = revenueCatData.ProductId,
                PurchaseDate = revenueCatData.PurchaseDate,
                ExpiresDate = revenueCatData.ExpiresDate,
                AutoRenewing = revenueCatData.AutoRenewing,
                Amount = revenueCatData.Price,
                Currency = revenueCatData.Currency
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayProvider] Error verifying transaction {TransactionId}", request.TransactionId);
            return new VerificationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Parse RevenueCat subscriber response and find matching transaction
    /// </summary>
    private RevenueCatSubscription? ParseRevenueCatSubscriber(string content, string transactionId)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(content);
            
            if (!json.RootElement.TryGetProperty("subscriber", out var subscriber) ||
                !subscriber.TryGetProperty("subscriptions", out var subscriptions))
            {
                return null;
            }
            
            foreach (var prop in subscriptions.EnumerateObject())
            {
                var subscription = prop.Value;
                
                if (!subscription.TryGetProperty("store_transaction_id", out var storeTransactionId))
                    continue;
                
                var txId = storeTransactionId.GetString();
                if (string.IsNullOrEmpty(txId))
                    continue;
                
                // Match transaction ID (could be exact match or prefix match)
                if (txId == transactionId || txId.StartsWith(transactionId))
                {
                    // Check for valid price (must be > 0 for paid transaction)
                    decimal? price = null;
                    string? currency = null;
                    if (subscription.TryGetProperty("price", out var priceObj))
                    {
                        if (priceObj.TryGetProperty("amount", out var amount))
                            price = (decimal)amount.GetDouble();
                        if (priceObj.TryGetProperty("currency", out var curr))
                            currency = curr.GetString();
                        
                        // Skip free/trial transactions
                        if (price <= 0)
                            continue;
                    }
                    
                    // Build product ID with plan identifier
                    var productId = prop.Name;
                    if (subscription.TryGetProperty("product_plan_identifier", out var planId) &&
                        !string.IsNullOrEmpty(planId.GetString()))
                    {
                        productId = $"{prop.Name}:{planId.GetString()}";
                    }
                    
                    return new RevenueCatSubscription
                    {
                        ProductId = productId,
                        OriginalTransactionId = txId,
                        PurchaseDate = subscription.TryGetProperty("purchase_date", out var purchDate)
                            ? DateTime.Parse(purchDate.GetString()!)
                            : DateTime.UtcNow,
                        ExpiresDate = subscription.TryGetProperty("expires_date", out var expDate) && expDate.ValueKind != System.Text.Json.JsonValueKind.Null
                            ? DateTime.Parse(expDate.GetString()!)
                            : null,
                        AutoRenewing = !subscription.TryGetProperty("unsubscribe_detected_at", out _),
                        Price = price,
                        Currency = currency
                    };
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayProvider] Error parsing RevenueCat response");
            return null;
        }
    }
    
    private class RevenueCatSubscription
    {
        public string ProductId { get; set; } = string.Empty;
        public string? OriginalTransactionId { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public DateTime? ExpiresDate { get; set; }
        public bool AutoRenewing { get; set; }
        public decimal? Price { get; set; }
        public string? Currency { get; set; }
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
    public string RevenueCatBaseUrl { get; set; } = "https://api.revenuecat.com/v1";
    public List<GoogleProductConfig> Products { get; set; } = new();
}

public class GoogleProductConfig
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Original plan type value from config (1=Day, 2=Month, 3=Year, 4=Week).
    /// This matches the legacy GodGPT PlanType enum values.
    /// </summary>
    public int PlanType { get; set; }
    public bool IsUltimate { get; set; }
}

