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
    private readonly IProductDataSource _productDataSource;

    public PaymentPlatform Platform => PaymentPlatform.GooglePlay;

    public GooglePlayProvider(
        ILogger<GooglePlayProvider> logger,
        IOptions<GooglePlayOptions> options,
        IHttpClientFactory httpClientFactory,
        IProductDataSource productDataSource)
    {
        _logger = logger;
        _options = options?.Value ?? throw new InvalidOperationException(
            $"GooglePlayOptions not configured. Ensure '{GooglePlayOptions.SectionName}' section exists in appsettings.json");
        _httpClient = httpClientFactory.CreateClient("GooglePlay");
        _productDataSource = productDataSource;
        
        // Log configuration status (similar to StripeProvider)
        _logger.LogDebug(
            "[GooglePlayProvider] Configuration loaded: RevenueCatApiKey={HasKey}, Products={ProductCount}",
            !string.IsNullOrEmpty(_options.RevenueCatApiKey),
            _options.Products?.Count ?? 0);
        
        if (string.IsNullOrEmpty(_options.RevenueCatApiKey))
        {
            _logger.LogInformation(
                "[GooglePlayProvider] RevenueCatApiKey is empty. RevenueCat API verification will be disabled. " +
                "Set {SectionName}:RevenueCatApiKey in appsettings.json to enable API verification.", 
                GooglePlayOptions.SectionName);
        }
    }

    public async Task<List<ProductDto>> GetProductsAsync(CancellationToken ct = default)
    {
        var products = await _productDataSource.GetProductsAsync(PaymentPlatform.GooglePlay, null, ct);
        if (products.Any())
        {
            _logger.LogDebug("[GooglePlayProvider] Retrieved {Count} products from data source", products.Count);
        }

        var productsFromConfiguration = GetProductsFromConfiguration();
        _logger.LogDebug("[GooglePlayProvider] Retrieved {Count} products from configuration", productsFromConfiguration.Count);
        
        products.AddRange(GetProductsFromConfiguration());
        return products;
    }

    private List<ProductDto> GetProductsFromConfiguration()
    {
        // Google Play products are configured in Play Console
        // Return configured products from options with originalPlanType metadata
        return _options.Products.Select(p => 
        {
            var billingCycle = p.GetBillingCycle();
            return new ProductDto
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Description = p.Description,
                Price = p.Amount,
                Currency = p.Currency,
                PlanType = p.IsUltimate ? PlanType.Premium : PlanType.Basic,
                BillingCycle = billingCycle,
                IsActive = true,
                Metadata = new Dictionary<string, string>
                {
                    ["originalPlanType"] = p.PlanType.ToString(),
                    ["isUltimate"] = p.IsUltimate.ToString().ToLower(),
                    ["dailyAvgPrice"] = CalculateDailyAvgPrice(p.Amount, billingCycle)
                }
            };
        }).ToList();
    }

    private static string CalculateDailyAvgPrice(decimal amount, BillingCycle cycle)
    {
        var days = cycle switch
        {
            BillingCycle.Daily => 1,
            BillingCycle.Weekly => 7,
            BillingCycle.Monthly => 30,
            BillingCycle.Quarterly => 90,
            BillingCycle.Yearly => 365,
            _ => 30
        };
        return Math.Round(amount / days, 2).ToString("F2");
    }

    /// <summary>
    /// Calculate real subscription end date based on product's PlanType.
    /// Tries config first, then IProductDataSource (e.g. Agent) when not in config.
    /// Google Play Sandbox may have short periods, production returns real dates.
    /// </summary>
    private async Task<DateTime?> CalculatePeriodEndFromProductAsync(string? productId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(productId))
            return null;

        BillingCycle? billingCycle = null;

        var configProduct = _options.Products.FirstOrDefault(p => p.ProductId == productId);
        if (configProduct != null)
            billingCycle = configProduct.GetBillingCycle();

        if (!billingCycle.HasValue)
        {
            var productFromSource = await _productDataSource.GetProductByPlatformProductIdAsync(PaymentPlatform.GooglePlay, productId, ct);
            if (productFromSource != null)
                billingCycle = productFromSource.BillingCycle;
        }

        if (!billingCycle.HasValue)
        {
            _logger.LogWarning("[GooglePlayProvider] Product {ProductId} not found in config or data source, using 30 days default", productId);
            return DateTime.UtcNow.AddDays(30);
        }

        var endDate = billingCycle.Value switch
        {
            BillingCycle.Daily => DateTime.UtcNow.AddDays(1),
            BillingCycle.Weekly => DateTime.UtcNow.AddDays(7),
            BillingCycle.Monthly => DateTime.UtcNow.AddDays(30),
            BillingCycle.Quarterly => DateTime.UtcNow.AddDays(90),
            BillingCycle.Yearly => DateTime.UtcNow.AddDays(390), // Match old code: 390 days for yearly
            _ => DateTime.UtcNow.AddDays(30)
        };

        _logger.LogInformation(
            "[GooglePlayProvider] Calculated PeriodEnd for {ProductId}: BillingCycle={Cycle}, EndDate={EndDate}",
            productId, billingCycle.Value, endDate);

        return endDate;
    }

    public async Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest request, 
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[GooglePlayProvider] CreateSubscription - UserId={UserId}, TxId={TxId}, IsSandbox={IsSandbox}",
            request.UserId, request.TransactionId, request.IsSandbox);
        
        // Google Play subscriptions are created via the app
        // Server-side just needs to verify the transaction
        if (string.IsNullOrEmpty(request.TransactionId) && string.IsNullOrEmpty(request.ReceiptData))
        {
            _logger.LogWarning("[GooglePlayProvider] CreateSubscription FAILED - TxId and ReceiptData empty");
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
            _logger.LogWarning("[GooglePlayProvider] CreateSubscription FAILED: {Error}", verification.ErrorMessage);
            return new SubscriptionResult
            {
                Success = false,
                ErrorMessage = verification.ErrorMessage
            };
        }

        var subscriptionId = verification.OriginalTransactionId ?? verification.TransactionId;
        _logger.LogInformation(
            "[GooglePlayProvider] CreateSubscription SUCCESS - UserId={UserId}, OrderId={OrderId}, ProductId={ProductId}",
            request.UserId, subscriptionId, verification.ProductId);
        
        return new SubscriptionResult
        {
            Success = true,
            SubscriptionId = subscriptionId,
            OrderId = subscriptionId, // Required for RecordPaymentAsync
            ProductId = verification.ProductId, // Required for RecordPaymentAsync product lookup
            ExpiresAt = verification.ExpiresDate,
            Status = PaymentStatus.Completed
        };
    }

    public async Task<VerificationResult> VerifyTransactionAsync(
        VerificationRequest request, 
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[GooglePlayProvider] VerifyTransaction - TxId={TxId}, UserId={UserId}",
            request.TransactionId, request.UserId);

        // Validate RevenueCat configuration
        if (string.IsNullOrEmpty(_options.RevenueCatApiKey))
        {
            _logger.LogWarning("[GooglePlayProvider] RevenueCatApiKey not configured - using webhook verification");
            return new VerificationResult
            {
                IsValid = true,
                TransactionId = request.TransactionId,
                ErrorMessage = "RevenueCat API key not configured - verification via webhook"
            };
        }

        if (request.UserId == Guid.Empty)
        {
            _logger.LogWarning("[GooglePlayProvider] VerifyTransaction FAILED - UserId empty");
            return new VerificationResult
            {
                IsValid = false,
                ErrorMessage = "UserId is required for Google Play verification"
            };
        }

        try
        {
            var requestUrl = $"{_options.RevenueCatBaseUrl}/subscribers/{request.UserId}";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.RevenueCatApiKey}");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            
            var response = await _httpClient.GetAsync(requestUrl, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[GooglePlayProvider] VerifyTransaction FAILED - StatusCode={StatusCode}, Error={Error}",
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
                _logger.LogWarning(
                    "[GooglePlayProvider] VerifyTransaction FAILED - TxId={TxId} not found for UserId={UserId}",
                    request.TransactionId, request.UserId);
                return new VerificationResult
                {
                    IsValid = false,
                    ErrorMessage = "Transaction not found in RevenueCat"
                };
            }
            
            var orderId = revenueCatData.OriginalTransactionId ?? request.TransactionId;
            _logger.LogInformation(
                "[GooglePlayProvider] VerifyTransaction SUCCESS - TxId={TxId}, OrderId={OrderId}, ProductId={ProductId}, ExpiresDate={ExpiresDate}",
                request.TransactionId, orderId, revenueCatData.ProductId, revenueCatData.ExpiresDate);

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
            _logger.LogError(ex, "[GooglePlayProvider] VerifyTransaction EXCEPTION - TxId={TxId}", request.TransactionId);
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

    public async Task<WebhookResult> HandleWebhookAsync(
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
                    return new WebhookResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid request headers"
                    };
                }
            }
            else
            {
                _logger.LogWarning("[GooglePlayProvider] Webhook header validation DISABLED (test mode)");
            }

            var webhookEvent = ParseRevenueCatWebhook(request.Payload);
            // OrderId = OriginalTransactionId (required for ProcessWebhookResultAsync)
            var orderId = webhookEvent.OriginalTransactionId ?? webhookEvent.TransactionId;

            if (!IsKeyBusinessEvent(webhookEvent))
            {
                return new WebhookResult
                {
                    Success = true,
                    ShouldProcess = false,
                    EventType = webhookEvent.EventType
                };
            }

            var result = new WebhookResult
            {
                Success = true,
                EventType = webhookEvent.EventType,
                TransactionId = webhookEvent.TransactionId,
                SubscriptionId = webhookEvent.OriginalTransactionId ?? webhookEvent.TransactionId,
                OrderId = orderId,
                ShouldProcess = true
            };

            // Extract user ID
            if (TryExtractUserId(webhookEvent, out var userId))
            {
                result.UserId = userId;
            }
            else
            {
                _logger.LogWarning("[GooglePlayProvider] Webhook missing UserId - AppUserId={AppUserId}", webhookEvent.AppUserId);
            }
            
            _logger.LogInformation(
                "[GooglePlayProvider] Webhook: Type={Type}, UserId={UserId}, OrderId={OrderId}, ProductId={ProductId}",
                webhookEvent.EventType, result.UserId, orderId, webhookEvent.ProductId);

            result.NewStatus = MapRevenueCatEventToStatus(webhookEvent.EventType);
            result.ProductId = webhookEvent.ProductId; // For product config lookup
            
            // For CANCELLATION events, distinguish between refund (Price < 0) and cancellation (Price = 0/null)
            // RevenueCat sends negative price for refunds within CANCELLATION event type
            if (webhookEvent.EventType == "CANCELLATION" && 
                webhookEvent.Price.HasValue && 
                webhookEvent.Price.Value < 0)
            {
                result.NewStatus = PaymentStatus.Refunded;
                _logger.LogInformation(
                    "[GooglePlayProvider] REFUND Webhook: UserId={UserId}, OrderId={OrderId}, " +
                    "ProductId={ProductId}, RefundAmount={RefundAmount}, CancelReason={CancelReason}",
                    result.UserId, orderId, webhookEvent.ProductId, 
                    webhookEvent.Price.Value, webhookEvent.CancelReason);
            }
            
            // Determine if this event should add a transaction record
            // Old code (UserBillingGAgent) called ProcessGooglePlayPurchaseSuccessAsync for:
            // - SUBSCRIPTION_PURCHASED -> RevenueCat: INITIAL_PURCHASE
            // - SUBSCRIPTION_RENEWED -> RevenueCat: RENEWAL
            // - SUBSCRIPTION_RECOVERED -> RevenueCat: UNCANCELLATION (resubscribe after cancel)
            // - SUBSCRIPTION_RESTARTED -> RevenueCat: UNCANCELLATION
            // - PRODUCT_CHANGE is similar to Apple's UPGRADE (weekly to monthly, etc.)
            // 
            // IMPORTANT: IsRenewal has TWO purposes:
            // 1. PaymentService: determines if we should add a transaction record (true = add)
            // 2. GodGPTPaymentBusinessService: determines if we should cancel old subscriptions (true = skip)
            // 
            // Event analysis:
            // - INITIAL_PURCHASE: First purchase of a NEW product (could be cross-platform upgrade)
            //   Should NOT be renewal for cancellation logic - may need to cancel Stripe/Apple subscriptions
            // - RENEWAL: Auto-renewal of same subscription - true (no cancel needed)
            // - UNCANCELLATION: Restore cancelled subscription - true (same subscription restored)
            // - PRODUCT_CHANGE: In-app upgrade/downgrade - true (Google handles old subscription)
            // 
            // For PaymentService transaction logic, all events should add transaction.
            // For GodGPTPaymentBusinessService cancellation logic, only INITIAL_PURCHASE needs cancel check.
            result.IsRenewal = webhookEvent.EventType == "RENEWAL" || 
                               webhookEvent.EventType == "UNCANCELLATION" ||
                               webhookEvent.EventType == "PRODUCT_CHANGE";

            result.VerificationResult = new VerificationResult
            {
                IsValid = true,
                TransactionId = webhookEvent.TransactionId,
                OriginalTransactionId = webhookEvent.OriginalTransactionId,
                ProductId = webhookEvent.ProductId,
                PurchaseDate = webhookEvent.PurchaseDate,
                ExpiresDate = webhookEvent.ExpiresDate, // Keep original for reference
                AutoRenewing = webhookEvent.AutoRenewing,
                Amount = webhookEvent.Price,
                Currency = webhookEvent.Currency
            };
            
            // Calculate real PeriodEnd based on PlanType (not platform ExpiresDate)
            // Google Play Sandbox may have short periods, production returns real dates
            // This matches old code behavior
            result.PeriodEnd = await CalculatePeriodEndFromProductAsync(webhookEvent.ProductId, ct);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayProvider] Webhook processing failed");
            return new WebhookResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
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
                ? DateTimeOffset.FromUnixTimeMilliseconds(purchMs.GetInt64()).UtcDateTime 
                : null,
            ExpiresDate = eventData.TryGetProperty("expiration_at_ms", out var expMs) && expMs.ValueKind != System.Text.Json.JsonValueKind.Null
                ? DateTimeOffset.FromUnixTimeMilliseconds(expMs.GetInt64()).UtcDateTime 
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
    public const string SectionName = "GooglePay";
    
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
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Original plan type value from config (1=Day, 2=Month, 3=Year, 4=Week).
    /// This matches the legacy GodGPT PlanType enum values.
    /// </summary>
    public int PlanType { get; set; }
    public bool IsUltimate { get; set; }
    
    /// <summary>
    /// Maps the legacy PlanType value to BillingCycle for internal calculations.
    /// Legacy PlanType: 1=Day, 2=Month, 3=Year, 4=Week
    /// BillingCycle: 1=Daily, 2=Weekly, 3=Monthly, 4=Quarterly, 5=Yearly
    /// </summary>
    public BillingCycle GetBillingCycle() => PlanType switch
    {
        1 => BillingCycle.Daily,    // Day -> Daily
        2 => BillingCycle.Monthly,  // Month -> Monthly
        3 => BillingCycle.Yearly,   // Year -> Yearly
        4 => BillingCycle.Weekly,   // Week -> Weekly
        _ => BillingCycle.Monthly   // Default to Monthly
    };
}

