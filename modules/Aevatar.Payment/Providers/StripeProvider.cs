using Aevatar.Payment.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Aevatar.Payment.Providers;

/// <summary>
/// Stripe payment provider implementation
/// </summary>
public class StripeProvider : IPaymentProvider
{
    private readonly ILogger<StripeProvider> _logger;
    private readonly StripeOptions _options;
    private readonly IStripeClient _client;

    public PaymentPlatform Platform => PaymentPlatform.Stripe;

    public StripeProvider(
        ILogger<StripeProvider> logger,
        IOptions<StripeOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        StripeConfiguration.ApiKey = _options.SecretKey;
        _client = new StripeClient(_options.SecretKey);
    }

    // ========== Product Operations ==========

    public Task<List<ProductDto>> GetProductsAsync(CancellationToken ct = default)
    {
        // Read from configuration (consistent with old UserBillingGAgent)
        if (_options.Products == null || !_options.Products.Any())
        {
            _logger.LogWarning("[StripeProvider] No products configured in StripeOptions");
            return Task.FromResult(new List<ProductDto>());
        }

        var products = _options.Products
            .Where(p => p.PlanType != BillingCycle.Daily) // Skip daily plans
            .Select(p => new ProductDto
            {
                ProductId = p.PriceId,
                Name = p.Name,
                Description = p.Description,
                Price = p.Amount,
                Currency = p.Currency,
                PlanType = p.IsUltimate ? PlanType.Premium : PlanType.Basic,
                BillingCycle = p.PlanType,
                IsActive = true,
                Metadata = new Dictionary<string, string>
                {
                    ["isUltimate"] = p.IsUltimate.ToString(),
                    ["mode"] = p.Mode,
                    ["dailyAvgPrice"] = CalculateDailyAvgPrice(p.Amount, p.PlanType)
                }
            })
            .ToList();

        _logger.LogDebug("[StripeProvider] Retrieved {Count} products from configuration", products.Count);
        return Task.FromResult(products);
    }

    // ========== Customer Operations ==========

    public async Task<CustomerResult> GetOrCreateCustomerAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        try
        {
            var customerService = new CustomerService(_client);
            var customerOptions = new CustomerCreateOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["internal_user_id"] = userId.ToString()
                }
            };

            var customer = await customerService.CreateAsync(customerOptions, cancellationToken: ct);
            
            _logger.LogInformation("[StripeProvider] Created Stripe Customer for user {UserId}: {CustomerId}",
                userId, customer.Id);

            return new CustomerResult
            {
                Success = true,
                CustomerId = customer.Id
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to create customer for user {UserId}", userId);
            return new CustomerResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CustomerSessionResult> GetCustomerSessionAsync(
        Guid userId,
        string customerId,
        CancellationToken ct = default)
    {
        try
        {
            var ephemeralKeyService = new EphemeralKeyService(_client);
            var ephemeralKeyOptions = new EphemeralKeyCreateOptions
            {
                Customer = customerId,
                StripeVersion = "2025-04-30.basil"
            };

            var ephemeralKey = await ephemeralKeyService.CreateAsync(ephemeralKeyOptions, cancellationToken: ct);
            
            _logger.LogInformation("[StripeProvider] Created EphemeralKey for user {UserId}, customer {CustomerId}",
                userId, customerId);

            return new CustomerSessionResult
            {
                Success = true,
                CustomerId = customerId,
                EphemeralKey = ephemeralKey.Secret,
                PublishableKey = _options.PublishableKey
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to create customer session for user {UserId}", userId);
            return new CustomerSessionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ========== Subscription Operations ==========

    public async Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest request, 
        CancellationToken ct = default)
    {
        try
        {
            var sessionService = new SessionService(_client);
            var sessionOptions = new SessionCreateOptions
            {
                Mode = request.Mode ?? "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Price = request.ProductId,
                        Quantity = 1
                    }
                },
                SuccessUrl = request.SuccessUrl ?? _options.SuccessUrl,
                CancelUrl = request.CancelUrl ?? _options.CancelUrl,
                Metadata = new Dictionary<string, string>
                {
                    ["user_id"] = request.UserId.ToString()
                },
                ClientReferenceId = request.UserId.ToString()
            };

            // Support embedded UI mode
            if (request.UiMode == "embedded")
            {
                sessionOptions.UiMode = "embedded";
                sessionOptions.ReturnUrl = request.SuccessUrl ?? _options.SuccessUrl;
            }

            // Attach customer if provided
            if (!string.IsNullOrEmpty(request.CustomerId))
            {
                sessionOptions.Customer = request.CustomerId;
            }

            // Add discount/coupon if provided
            if (!string.IsNullOrEmpty(request.CouponCode))
            {
                sessionOptions.Discounts = new List<SessionDiscountOptions>
                {
                    new() { Coupon = request.CouponCode }
                };
            }

            // Add trial period if provided
            if (request.TrialDays > 0)
            {
                sessionOptions.SubscriptionData = new SessionSubscriptionDataOptions
                {
                    TrialPeriodDays = request.TrialDays
                };
            }

            var session = await sessionService.CreateAsync(sessionOptions, cancellationToken: ct);

            _logger.LogInformation("[StripeProvider] Created checkout session {SessionId} for user {UserId}",
                session.Id, request.UserId);

            return new SubscriptionResult
            {
                Success = true,
                SessionUrl = session.Url,
                SubscriptionId = session.Id,
                Status = PaymentStatus.Pending,
                AdditionalData = new Dictionary<string, object>
                {
                    ["clientSecret"] = session.ClientSecret ?? string.Empty
                }
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to create subscription for user {UserId}", request.UserId);
            return new SubscriptionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<PaymentSheetResult> CreatePaymentSheetAsync(
        PaymentSheetRequest request,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Create EphemeralKey
            var ephemeralKeyService = new EphemeralKeyService(_client);
            var ephemeralKey = await ephemeralKeyService.CreateAsync(new EphemeralKeyCreateOptions
            {
                Customer = request.CustomerId,
                StripeVersion = "2025-04-30.basil"
            }, cancellationToken: ct);

            // 2. Create PaymentIntent
            var paymentIntentService = new PaymentIntentService(_client);
            var paymentIntent = await paymentIntentService.CreateAsync(new PaymentIntentCreateOptions
            {
                Amount = request.Amount,
                Currency = request.Currency,
                Customer = request.CustomerId,
                Description = request.Description,
                Metadata = new Dictionary<string, string>
                {
                    ["internal_user_id"] = request.UserId.ToString(),
                    ["order_id"] = request.OrderId ?? Guid.NewGuid().ToString(),
                    ["price_id"] = request.PriceId ?? string.Empty
                },
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                }
            }, cancellationToken: ct);

            _logger.LogInformation("[StripeProvider] Created PaymentSheet for user {UserId}, intent {IntentId}",
                request.UserId, paymentIntent.Id);

            return new PaymentSheetResult
            {
                Success = true,
                PaymentIntentClientSecret = paymentIntent.ClientSecret,
                EphemeralKey = ephemeralKey.Secret,
                CustomerId = request.CustomerId,
                PublishableKey = _options.PublishableKey
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to create PaymentSheet for user {UserId}", request.UserId);
            return new PaymentSheetResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ========== Verification ==========

    public async Task<VerificationResult> VerifyTransactionAsync(
        VerificationRequest request, 
        CancellationToken ct = default)
    {
        try
        {
            var invoiceService = new InvoiceService(_client);
            var invoice = await invoiceService.GetAsync(request.TransactionId, cancellationToken: ct);

            return new VerificationResult
            {
                IsValid = invoice.Status == "paid",
                TransactionId = invoice.Id,
                Amount = invoice.AmountPaid / 100m,
                Currency = invoice.Currency.ToUpper()
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to verify transaction {TxId}", request.TransactionId);
            return new VerificationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ========== Webhook ==========

    public async Task<WebhookResult> HandleWebhookAsync(
        WebhookRequest request, 
        CancellationToken ct = default)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                request.Payload,
                request.Signature,
                _options.WebhookSecret
            );

            _logger.LogInformation("[StripeProvider] Processing webhook: {EventType}", stripeEvent.Type);

            var result = new WebhookResult
            {
                Success = true,
                EventType = stripeEvent.Type
            };

            // Extract user ID from metadata
            var userId = ExtractUserId(stripeEvent);
            if (userId.HasValue)
            {
                result.UserId = userId;
            }

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutCompleted(stripeEvent, result);
                    break;
                case "invoice.paid":
                    await HandleInvoicePaid(stripeEvent, result);
                    break;
                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated(stripeEvent, result);
                    break;
                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted(stripeEvent, result);
                    break;
                case "payment_intent.succeeded":
                    await HandlePaymentIntentSucceeded(stripeEvent, result);
                    break;
                default:
                    result.ShouldProcess = false;
                    break;
            }

            return result;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Webhook validation failed");
            return new WebhookResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ========== Cancellation ==========

    public async Task<CancellationResult> CancelSubscriptionAsync(
        CancellationRequest request, 
        CancellationToken ct = default)
    {
        try
        {
            var service = new SubscriptionService(_client);

            if (request.Immediate)
            {
                await service.CancelAsync(request.SubscriptionId, cancellationToken: ct);
                return new CancellationResult
                {
                    Success = true,
                    EffectiveDate = DateTime.UtcNow
                };
            }

            var options = new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true,
                Metadata = new Dictionary<string, string>
                {
                    ["cancel_reason"] = request.Reason ?? "User requested"
                }
            };

            var subscription = await service.UpdateAsync(request.SubscriptionId, options, cancellationToken: ct);
            
            return new CancellationResult
            {
                Success = true,
                EffectiveDate = subscription.EndedAt ?? DateTime.UtcNow.AddMonths(1)
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to cancel subscription {SubId}", request.SubscriptionId);
            return new CancellationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ========== Status ==========

    public async Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(
        string subscriptionId, 
        CancellationToken ct = default)
    {
        try
        {
            var service = new SubscriptionService(_client);
            var subscription = await service.GetAsync(subscriptionId, cancellationToken: ct);

            var periodEnd = subscription.EndedAt 
                         ?? (subscription.CancelAt.HasValue ? subscription.CancelAt : null);
            
            return new SubscriptionStatusResult
            {
                IsActive = subscription.Status == "active",
                SubscriptionId = subscription.Id,
                Status = MapStripeStatus(subscription.Status),
                CurrentPeriodEnd = periodEnd,
                WillRenew = !subscription.CancelAtPeriodEnd,
                ProductId = subscription.Items.Data.FirstOrDefault()?.Price.Id
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to get subscription status {SubId}", subscriptionId);
            return new SubscriptionStatusResult
            {
                IsActive = false,
                Status = PaymentStatus.Failed
            };
        }
    }

    #region Private Helpers

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

    private static Guid? ExtractUserId(Event stripeEvent)
    {
        string? userIdStr = null;

        if (stripeEvent.Data.Object is Session session)
        {
            userIdStr = session.ClientReferenceId ?? session.Metadata?.GetValueOrDefault("user_id");
        }
        else if (stripeEvent.Data.Object is Invoice invoice)
        {
            userIdStr = invoice.Metadata?.GetValueOrDefault("user_id");
        }
        else if (stripeEvent.Data.Object is Subscription subscription)
        {
            userIdStr = subscription.Metadata?.GetValueOrDefault("user_id");
        }
        else if (stripeEvent.Data.Object is PaymentIntent paymentIntent)
        {
            userIdStr = paymentIntent.Metadata?.GetValueOrDefault("internal_user_id");
        }

        return Guid.TryParse(userIdStr, out var userId) ? userId : null;
    }

    private Task HandleCheckoutCompleted(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Session session)
        {
            result.SubscriptionId = session.SubscriptionId ?? session.Id;
            result.NewStatus = PaymentStatus.Completed;
        }
        return Task.CompletedTask;
    }

    private Task HandleInvoicePaid(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Invoice invoice)
        {
            var subscriptionId = invoice.Parent?.SubscriptionDetails?.Subscription?.Id;
            
            result.TransactionId = invoice.Id;
            result.SubscriptionId = subscriptionId;
            result.NewStatus = PaymentStatus.Completed;
            result.VerificationResult = new VerificationResult
            {
                IsValid = true,
                TransactionId = invoice.Id,
                OriginalTransactionId = subscriptionId,
                Amount = invoice.AmountPaid / 100m,
                Currency = invoice.Currency?.ToUpper() ?? "USD",
                PurchaseDate = invoice.Created
            };
        }
        return Task.CompletedTask;
    }

    private Task HandleSubscriptionUpdated(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Subscription subscription)
        {
            result.SubscriptionId = subscription.Id;
            result.NewStatus = MapStripeStatus(subscription.Status);
        }
        return Task.CompletedTask;
    }

    private Task HandleSubscriptionDeleted(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Subscription subscription)
        {
            result.SubscriptionId = subscription.Id;
            result.NewStatus = PaymentStatus.Cancelled;
        }
        return Task.CompletedTask;
    }

    private Task HandlePaymentIntentSucceeded(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is PaymentIntent paymentIntent)
        {
            result.TransactionId = paymentIntent.Id;
            result.NewStatus = PaymentStatus.Completed;
            result.VerificationResult = new VerificationResult
            {
                IsValid = true,
                TransactionId = paymentIntent.Id,
                Amount = paymentIntent.Amount / 100m,
                Currency = paymentIntent.Currency?.ToUpper() ?? "USD",
                PurchaseDate = paymentIntent.Created
            };
        }
        return Task.CompletedTask;
    }

    private static PaymentStatus MapStripeStatus(string stripeStatus)
    {
        return stripeStatus switch
        {
            "active" => PaymentStatus.Completed,
            "past_due" => PaymentStatus.Pending,
            "canceled" => PaymentStatus.Cancelled,
            "unpaid" => PaymentStatus.Failed,
            _ => PaymentStatus.Pending
        };
    }

    #endregion
}

/// <summary>
/// Stripe configuration options
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";
    
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Configured products (read from appsettings)
    /// </summary>
    public List<StripeProductConfig> Products { get; set; } = new();
}

/// <summary>
/// Stripe product configuration
/// </summary>
public class StripeProductConfig
{
    public string PriceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public BillingCycle PlanType { get; set; }
    public string Mode { get; set; } = "subscription";
    public bool IsUltimate { get; set; }
}
