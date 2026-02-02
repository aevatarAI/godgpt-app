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
        
        // Support test mode without API key (only webhook parsing works)
        if (!string.IsNullOrEmpty(_options.SecretKey))
        {
            StripeConfiguration.ApiKey = _options.SecretKey;
            _client = new StripeClient(_options.SecretKey);
        }
        else
        {
            _logger.LogWarning("[StripeProvider] SecretKey is empty, running in test mode (webhook-only)");
            _client = null!;
        }
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
            .Where(p => p.PlanType != 1) // Skip daily plans (PlanType 1 = Day)
            .Select(p => 
            {
                var billingCycle = p.GetBillingCycle();
                return new ProductDto
                {
                    ProductId = p.PriceId,
                    Name = p.Name,
                    Description = p.Description,
                    Price = p.Amount,
                    Currency = p.Currency,
                    PlanType = p.IsUltimate ? PlanType.Premium : PlanType.Basic,
                    BillingCycle = billingCycle,
                    IsActive = true,
                    Metadata = new Dictionary<string, string>
                    {
                        ["isUltimate"] = p.IsUltimate.ToString().ToLower(),
                        ["mode"] = p.Mode,
                        ["originalPlanType"] = p.PlanType.ToString(),
                        ["dailyAvgPrice"] = CalculateDailyAvgPrice(p.Amount, billingCycle)
                    }
                };
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
            // Check if Stripe client is configured
            if (_client == null)
            {
                _logger.LogError("[StripeProvider] Stripe client not configured (test mode)");
                return new SubscriptionResult
                {
                    Success = false,
                    ErrorMessage = "Stripe is not configured"
                };
            }
            
            var sessionService = new SessionService(_client);
            
            // Use IsNullOrEmpty to handle both null and empty string from client
            var successUrl = string.IsNullOrEmpty(request.SuccessUrl) ? _options.SuccessUrl : request.SuccessUrl;
            var cancelUrl = string.IsNullOrEmpty(request.CancelUrl) ? _options.CancelUrl : request.CancelUrl;
            
            // Validate URLs are configured - Stripe requires non-empty URLs
            if (string.IsNullOrEmpty(successUrl) || string.IsNullOrEmpty(cancelUrl))
            {
                _logger.LogError("[StripeProvider] SuccessUrl or CancelUrl is not configured. " +
                    "SuccessUrl: '{SuccessUrl}', CancelUrl: '{CancelUrl}'", successUrl, cancelUrl);
                return new SubscriptionResult
                {
                    Success = false,
                    ErrorMessage = "Payment URLs are not properly configured"
                };
            }
            
            // Generate stable order_id for PaymentRecordGAgent lookup
            var orderId = request.Metadata.GetValueOrDefault("order_id") ?? Guid.NewGuid().ToString();
            
            // Common metadata for session, subscription, and invoice
            var commonMetadata = new Dictionary<string, string>
            {
                ["internal_user_id"] = request.UserId.ToString(),
                ["order_id"] = orderId,
                ["price_id"] = request.ProductId ?? string.Empty,
                ["quantity"] = "1"
            };
            
            var mode = request.Mode ?? "subscription";
            var sessionOptions = new SessionCreateOptions
            {
                Mode = mode,
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Price = request.ProductId,
                        Quantity = 1
                    }
                },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = commonMetadata,
                ClientReferenceId = request.UserId.ToString(),
                // Match old code logic: conditionally set metadata carrier based on mode
                // Stripe API doesn't allow PaymentIntentData in subscription mode
                PaymentIntentData = mode == "payment"
                    ? new SessionPaymentIntentDataOptions
                    {
                        SetupFutureUsage = "off_session",
                        Metadata = commonMetadata
                    }
                    : null,
                SubscriptionData = mode == "subscription"
                    ? new SessionSubscriptionDataOptions
                    {
                        Metadata = commonMetadata
                    }
                    : null
            };

            // Support embedded UI mode
            if (request.UiMode == "embedded")
            {
                sessionOptions.UiMode = "embedded";
                sessionOptions.ReturnUrl = successUrl; // Already validated above
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
                sessionOptions.SubscriptionData.TrialPeriodDays = request.TrialDays;
            }

            // Log metadata setup for debugging
            var subDataMetaKeys = sessionOptions.SubscriptionData?.Metadata != null 
                ? string.Join(",", sessionOptions.SubscriptionData.Metadata.Keys) 
                : "(null)";
            var subDataMetaValues = sessionOptions.SubscriptionData?.Metadata != null 
                ? string.Join(",", sessionOptions.SubscriptionData.Metadata.Values) 
                : "(null)";
            _logger.LogInformation(
                "[StripeProvider] Creating checkout session: Mode={Mode}, OrderId={OrderId}, " +
                "HasSubscriptionData={HasSubscriptionData}, " +
                "SubscriptionData.Metadata.Keys={SubDataMetaKeys}, " +
                "SubscriptionData.Metadata.Values={SubDataMetaValues}",
                mode, orderId,
                sessionOptions.SubscriptionData != null,
                subDataMetaKeys,
                subDataMetaValues);
            
            var session = await sessionService.CreateAsync(sessionOptions, cancellationToken: ct);

            _logger.LogInformation("[StripeProvider] Created checkout session {SessionId} for user {UserId}, OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
                session.Id, request.UserId, orderId, session.PaymentIntentId ?? "(null)");

            return new SubscriptionResult
            {
                Success = true,
                SessionUrl = session.Url,
                SubscriptionId = null, // Real subscriptionId (sub_xxx) comes from webhook, not session.Id (cs_test_xxx)
                OrderId = orderId, // Return orderId so PaymentService can use it consistently
                Status = PaymentStatus.Pending,
                AdditionalData = new Dictionary<string, object>
                {
                    ["sessionId"] = session.Id, // Store sessionId in AdditionalData for reference
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
            Event stripeEvent;
            
            // Allow bypassing signature verification for testing
            // Set WebhookSecret to empty or "test" to skip verification
            if (string.IsNullOrEmpty(_options.WebhookSecret) || _options.WebhookSecret == "test")
            {
                _logger.LogWarning("[StripeProvider] Webhook signature verification DISABLED (test mode)");
                try
                {
                    stripeEvent = EventUtility.ParseEvent(request.Payload);
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "[StripeProvider] Failed to parse event with Stripe SDK, attempting JSON fallback");
                    // Fallback: Parse as raw JSON for testing purposes
                    return ParseEventFromJson(request.Payload);
                }
            }
            else
            {
                stripeEvent = EventUtility.ConstructEvent(
                    request.Payload,
                    request.Signature,
                    _options.WebhookSecret
                );
            }

            _logger.LogInformation("[StripeProvider] Processing webhook: {EventType}", stripeEvent.Type);

            var result = new WebhookResult
            {
                Success = true,
                EventType = stripeEvent.Type,
                ShouldProcess = true  // Default to true, set to false for unhandled events
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
                case "charge.refunded":
                    await HandleChargeRefunded(stripeEvent, result);
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
        else if (stripeEvent.Data.Object is Charge charge)
        {
            // Try Charge metadata first, then PaymentIntent metadata
            userIdStr = charge.Metadata?.GetValueOrDefault("user_id")
                ?? charge.Metadata?.GetValueOrDefault("internal_user_id")
                ?? charge.PaymentIntent?.Metadata?.GetValueOrDefault("user_id")
                ?? charge.PaymentIntent?.Metadata?.GetValueOrDefault("internal_user_id");
        }

        return Guid.TryParse(userIdStr, out var userId) ? userId : null;
    }

    private Task HandleCheckoutCompleted(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Session session)
        {
            // Extract orderId from metadata (stable key for PaymentRecordGAgent)
            result.OrderId = TryGetFromMetadata(session.Metadata, "order_id");
            result.SubscriptionId = session.SubscriptionId;
            // NOTE: Don't set NewStatus = Completed here!
            // checkout.session.completed only means user completed checkout flow.
            // invoice.paid is the actual payment success signal and should trigger PaymentCompletedEvent.
            // Setting Completed here causes duplicate events (one from checkout, one from invoice.paid).
            result.NewStatus = PaymentStatus.Processing;
            
            _logger.LogInformation(
                "[StripeProvider] checkout.session.completed: OrderId={OrderId}, SubscriptionId={SubscriptionId}, Status=Processing (waiting for invoice.paid)",
                result.OrderId, session.SubscriptionId);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Parse Stripe event from raw JSON (fallback for test mode when SDK parsing fails)
    /// </summary>
    private WebhookResult ParseEventFromJson(string payload)
    {
        try
        {
            var jsonEvent = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payload);
            var eventType = jsonEvent.GetProperty("type").GetString();
            _logger.LogInformation("[StripeProvider] Parsing event from JSON fallback: {EventType}", eventType);

            var result = new WebhookResult
            {
                Success = true,
                EventType = eventType,
                ShouldProcess = true
            };

            // Extract data.object
            if (jsonEvent.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj))
            {
                // Extract userId and orderId from metadata (multiple locations)
                string? userId = null;
                string? orderId = null;
                
                // Try subscription_details.metadata first (for invoice events)
                if (obj.TryGetProperty("subscription_details", out var subDetails) &&
                    subDetails.TryGetProperty("metadata", out var subMeta))
                {
                    userId = TryGetJsonString(subMeta, "internal_user_id") ?? TryGetJsonString(subMeta, "userId");
                    orderId = TryGetJsonString(subMeta, "order_id");
                }
                // Also try parent.subscription_details.metadata (Stripe SDK format)
                else if (obj.TryGetProperty("parent", out var parent) &&
                    parent.TryGetProperty("subscription_details", out var parentSubDetails) &&
                    parentSubDetails.TryGetProperty("metadata", out var parentMeta))
                {
                    userId = TryGetJsonString(parentMeta, "internal_user_id") ?? TryGetJsonString(parentMeta, "userId");
                    orderId = TryGetJsonString(parentMeta, "order_id");
                }
                // Try direct metadata (for checkout.session events)
                if (obj.TryGetProperty("metadata", out var meta))
                {
                    userId ??= TryGetJsonString(meta, "internal_user_id") ?? TryGetJsonString(meta, "userId");
                    orderId ??= TryGetJsonString(meta, "order_id");
                }

                if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
                {
                    result.UserId = userGuid;
                }
                result.OrderId = orderId;

                // Extract subscription ID
                if (obj.TryGetProperty("subscription", out var subId))
                {
                    result.SubscriptionId = subId.GetString();
                }

                // Extract invoice ID as transaction ID
                if (obj.TryGetProperty("id", out var invoiceId))
                {
                    result.TransactionId = invoiceId.GetString();
                }

                // Extract priceId from lines
                if (obj.TryGetProperty("lines", out var lines) &&
                    lines.TryGetProperty("data", out var linesData) &&
                    linesData.GetArrayLength() > 0)
                {
                    var firstLine = linesData[0];
                    if (firstLine.TryGetProperty("pricing", out var pricing) &&
                        pricing.TryGetProperty("price_details", out var priceDetails) &&
                        priceDetails.TryGetProperty("price", out var priceId))
                    {
                        result.ProductId = priceId.GetString();
                    }
                }

                // Determine if renewal
                if (obj.TryGetProperty("billing_reason", out var billingReason))
                {
                    result.IsRenewal = billingReason.GetString() == "subscription_cycle";
                }

                // Set NewStatus based on event type
                if (eventType == "invoice.paid")
                {
                    result.NewStatus = PaymentStatus.Completed;
                }
                else if (eventType == "charge.refunded")
                {
                    // For charge.refunded in JSON fallback mode:
                    // charge.metadata is usually empty, and we can't call Stripe API in test mode
                    // In production, HandleChargeRefunded fetches metadata from PaymentIntent or Invoice->Subscription
                    result.NewStatus = PaymentStatus.Refunded;
                    
                    // Extract charge-specific fields
                    if (obj.TryGetProperty("amount_refunded", out var amountRefunded))
                    {
                        result.VerificationResult = new VerificationResult
                        {
                            IsValid = true,
                            Amount = amountRefunded.GetInt64() / 100m,
                            TransactionId = result.TransactionId
                        };
                    }
                    
                    _logger.LogWarning(
                        "[StripeProvider] charge.refunded in JSON fallback mode: OrderId/UserId may be null. " +
                        "In production (with signature verification), HandleChargeRefunded fetches metadata from PaymentIntent or Subscription. " +
                        "ChargeId={ChargeId}, RefundAmount={RefundAmount}",
                        result.TransactionId, result.VerificationResult?.Amount);
                }

                _logger.LogInformation(
                    "[StripeProvider] JSON fallback parsed: OrderId={OrderId}, UserId={UserId}, SubscriptionId={SubscriptionId}, ProductId={ProductId}, IsRenewal={IsRenewal}",
                    result.OrderId, result.UserId, result.SubscriptionId, result.ProductId, result.IsRenewal);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StripeProvider] Failed to parse JSON fallback");
            return new WebhookResult
            {
                Success = false,
                ErrorMessage = $"JSON parse failed: {ex.Message}"
            };
        }
    }

    private Task HandleInvoicePaid(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Invoice invoice)
        {
            // Try multiple paths to get subscription info (Stripe SDK structure varies)
            var subscriptionId = invoice.Parent?.SubscriptionDetails?.Subscription?.Id 
                ?? invoice.Parent?.SubscriptionDetails?.SubscriptionId; // Fallback path
            var subscriptionMetadata = invoice.Parent?.SubscriptionDetails?.Metadata;
            
            // Debug: Log subscription metadata from invoice
            _logger.LogInformation(
                "[StripeProvider] invoice.paid: METADATA DEBUG - InvoiceId={InvoiceId}, SubscriptionId={SubscriptionId}, " +
                "MetadataCount={MetadataCount}, MetadataKeys={MetadataKeys}",
                invoice.Id,
                subscriptionId ?? "(null)",
                subscriptionMetadata?.Count ?? 0,
                subscriptionMetadata != null ? string.Join(",", subscriptionMetadata.Keys) : "(null)");
            
            // Extract orderId from subscription metadata (stable key)
            result.OrderId = TryGetFromMetadata(subscriptionMetadata, "order_id");
            
            // Extract userId from subscription metadata (required for creating payment record)
            var userIdStr = TryGetFromMetadata(subscriptionMetadata, "internal_user_id");
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var userId))
            {
                result.UserId = userId;
            }
            
            // Extract priceId from invoice line items
            // Stripe.net 48.x uses Pricing.PriceDetails.Price for price info (returns string ID)
            var lineItem = invoice.Lines?.Data?.FirstOrDefault();
            string? priceId = null;
            if (lineItem?.Pricing?.Type == "price_details" && !string.IsNullOrEmpty(lineItem.Pricing.PriceDetails?.Price))
            {
                priceId = lineItem.Pricing.PriceDetails.Price;
            }
            
            // Determine if this is a renewal based on billing_reason
            // - subscription_create: First-time subscription
            // - subscription_cycle: Renewal payment
            var isRenewal = invoice.BillingReason == "subscription_cycle";
            
            // Extract period end from invoice line item
            DateTime? periodEnd = null;
            if (lineItem?.Period?.End != null)
            {
                periodEnd = lineItem.Period.End;
            }
            
            result.TransactionId = invoice.Id;
            result.SubscriptionId = subscriptionId;
            result.NewStatus = PaymentStatus.Completed;
            result.ProductId = priceId; // Use priceId for Stripe product lookup
            result.IsRenewal = isRenewal;
            result.PeriodEnd = periodEnd;
            result.VerificationResult = new VerificationResult
            {
                IsValid = true,
                TransactionId = invoice.Id,
                OriginalTransactionId = subscriptionId,
                ProductId = priceId, // Also store in VerificationResult
                Amount = invoice.AmountPaid / 100m,
                Currency = invoice.Currency?.ToUpper() ?? "USD",
                PurchaseDate = invoice.Created
            };
            
            _logger.LogInformation(
                "[StripeProvider] invoice.paid: OrderId={OrderId}, SubscriptionId={SubscriptionId}, IsRenewal={IsRenewal}",
                result.OrderId, subscriptionId, isRenewal);
        }
        return Task.CompletedTask;
    }

    private Task HandleSubscriptionUpdated(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Subscription subscription)
        {
            // Debug: Log all subscription metadata
            _logger.LogInformation(
                "[StripeProvider] subscription.updated: METADATA DEBUG - SubscriptionId={SubscriptionId}, " +
                "MetadataCount={MetadataCount}, MetadataKeys={MetadataKeys}, MetadataValues={MetadataValues}",
                subscription.Id,
                subscription.Metadata?.Count ?? 0,
                subscription.Metadata != null ? string.Join(",", subscription.Metadata.Keys) : "(null)",
                subscription.Metadata != null ? string.Join(",", subscription.Metadata.Values) : "(null)");
            
            result.OrderId = TryGetFromMetadata(subscription.Metadata, "order_id");
            result.SubscriptionId = subscription.Id;
            
            // Also extract UserId from subscription metadata
            var userIdStr = TryGetFromMetadata(subscription.Metadata, "internal_user_id");
            if (Guid.TryParse(userIdStr, out var userId))
            {
                result.UserId = userId;
            }
            
            // Match old code logic: if subscription is canceled OR auto-renewal was cancelled
            // (CancelAtPeriodEnd=true), set status to Cancelled directly
            if (subscription.Status == "canceled" || subscription.CancelAtPeriodEnd)
            {
                result.NewStatus = PaymentStatus.Cancelled; // Same as old code - direct Cancelled
                _logger.LogInformation(
                    "[StripeProvider] subscription.updated: Subscription {SubscriptionId} cancelled (Status={Status}, CancelAtPeriodEnd={CancelAtPeriodEnd})",
                    subscription.Id, subscription.Status, subscription.CancelAtPeriodEnd);
            }
            else
            {
                result.NewStatus = MapStripeStatus(subscription.Status);
            }
            
            _logger.LogInformation(
                "[StripeProvider] subscription.updated: OrderId={OrderId}, UserId={UserId}, SubscriptionId={SubscriptionId}, Status={Status}, CancelAtPeriodEnd={CancelAtPeriodEnd}",
                result.OrderId, result.UserId, subscription.Id, subscription.Status, subscription.CancelAtPeriodEnd);
        }
        return Task.CompletedTask;
    }

    private Task HandleSubscriptionDeleted(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Subscription subscription)
        {
            result.OrderId = TryGetFromMetadata(subscription.Metadata, "order_id");
            result.SubscriptionId = subscription.Id;
            result.NewStatus = PaymentStatus.Cancelled;
            
            _logger.LogInformation(
                "[StripeProvider] subscription.deleted: OrderId={OrderId}, SubscriptionId={SubscriptionId}",
                result.OrderId, subscription.Id);
        }
        return Task.CompletedTask;
    }

    private Task HandlePaymentIntentSucceeded(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is PaymentIntent paymentIntent)
        {
            // Extract OrderId from metadata for PaymentRecordGAgent lookup
            result.OrderId = TryGetFromMetadata(paymentIntent.Metadata, "order_id");
            
            // PaymentIntent is typically used for one-time payments, not subscriptions
            // If it's associated with a subscription, the subscriptionId would be in metadata
            result.SubscriptionId = TryGetFromMetadata(paymentIntent.Metadata, "subscription_id");
            
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
            
            _logger.LogInformation(
                "[StripeProvider] payment_intent.succeeded: OrderId={OrderId}, TransactionId={TransactionId}, SubscriptionId={SubscriptionId}",
                result.OrderId, paymentIntent.Id, result.SubscriptionId);
        }
        return Task.CompletedTask;
    }

    private async Task HandleChargeRefunded(Event stripeEvent, WebhookResult result)
    {
        if (stripeEvent.Data.Object is Charge charge)
        {
            // ========== DIAGNOSTIC LOGGING ==========
            // Log all charge details for debugging metadata retrieval issues
            // Note: SDK 48.x removed Charge.InvoiceId property, use RawJObject
            var chargeInvoiceId = charge.RawJObject?.SelectToken("invoice")?.ToString();
            _logger.LogInformation(
                "[StripeProvider] charge.refunded: === DIAGNOSTIC START === " +
                "ChargeId={ChargeId}, PaymentIntentId={PaymentIntentId}, InvoiceId={InvoiceId}, " +
                "ChargeMetadataCount={ChargeMetadataCount}, ChargeMetadataKeys={ChargeMetadataKeys}",
                charge.Id, 
                charge.PaymentIntentId ?? "(null)",
                chargeInvoiceId ?? "(null)",
                charge.Metadata?.Count ?? 0,
                charge.Metadata != null ? string.Join(",", charge.Metadata.Keys) : "(null)");
            
            // Try multiple sources for metadata (same as old code):
            // 1. Charge.Metadata (rarely populated)
            // 2. PaymentIntent.Metadata (for payment mode)
            // 3. Charge -> Invoice -> Subscription.Metadata (for subscription mode)
            var metadata = charge.Metadata;
            string metadataSource = "Charge";
            
            // If Charge metadata is empty, fetch PaymentIntent from Stripe API
            if ((metadata == null || !metadata.Any() || !metadata.ContainsKey("order_id")) 
                && !string.IsNullOrEmpty(charge.PaymentIntentId))
            {
                try
                {
                    var paymentIntentService = new PaymentIntentService(_client);
                    var paymentIntent = await paymentIntentService.GetAsync(charge.PaymentIntentId);
                    
                    _logger.LogInformation(
                        "[StripeProvider] charge.refunded: PaymentIntent lookup - " +
                        "PaymentIntentId={PaymentIntentId}, MetadataCount={MetadataCount}, MetadataKeys={MetadataKeys}",
                        charge.PaymentIntentId,
                        paymentIntent?.Metadata?.Count ?? 0,
                        paymentIntent?.Metadata != null ? string.Join(",", paymentIntent.Metadata.Keys) : "(null)");
                    
                    if (paymentIntent?.Metadata != null && paymentIntent.Metadata.ContainsKey("order_id"))
                    {
                        metadata = paymentIntent.Metadata;
                        metadataSource = "PaymentIntent";
                        _logger.LogDebug(
                            "[StripeProvider] charge.refunded: Using PaymentIntent metadata {PaymentIntentId}",
                            charge.PaymentIntentId);
                    }
                }
                catch (StripeException ex)
                {
                    _logger.LogWarning(ex,
                        "[StripeProvider] charge.refunded: Failed to fetch PaymentIntent {PaymentIntentId}",
                        charge.PaymentIntentId);
                }
            }
            
            // If still no metadata, get Subscription directly from Customer
            // Same approach as subscription.updated - Subscription.Metadata is the source of truth
            // Invoice path doesn't work in API 2025-04-30.basil (invoice.payment_intent/subscription are null)
            if ((metadata == null || !metadata.Any() || !metadata.ContainsKey("order_id"))
                && !string.IsNullOrEmpty(charge.CustomerId))
            {
                try
                {
                    var subscriptionService = new SubscriptionService(_client);
                    var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
                    {
                        Customer = charge.CustomerId,
                        Limit = 10,
                        Status = "all" // Include canceled subscriptions
                    });
                    
                    _logger.LogInformation(
                        "[StripeProvider] charge.refunded: Customer Subscription lookup - " +
                        "CustomerId={CustomerId}, SubscriptionCount={Count}",
                        charge.CustomerId, subscriptions.Data.Count);
                    
                    // Find subscription with order_id metadata
                    foreach (var sub in subscriptions.Data)
                    {
                        _logger.LogInformation(
                            "[StripeProvider] charge.refunded: Checking Subscription - " +
                            "SubscriptionId={SubscriptionId}, Status={Status}, " +
                            "MetadataCount={MetadataCount}, MetadataKeys={MetadataKeys}",
                            sub.Id, sub.Status,
                            sub.Metadata?.Count ?? 0,
                            sub.Metadata != null ? string.Join(",", sub.Metadata.Keys) : "(null)");
                        
                        if (sub.Metadata != null && sub.Metadata.ContainsKey("order_id"))
                        {
                            metadata = sub.Metadata;
                            metadataSource = $"Subscription({sub.Id})";
                            _logger.LogInformation(
                                "[StripeProvider] charge.refunded: Found metadata from Subscription {SubscriptionId}",
                                sub.Id);
                            break;
                        }
                    }
                }
                catch (StripeException ex)
                {
                    _logger.LogWarning(ex,
                        "[StripeProvider] charge.refunded: Failed to lookup Customer subscriptions {CustomerId}",
                        charge.CustomerId);
                }
            }
            
            _logger.LogInformation(
                "[StripeProvider] charge.refunded: === DIAGNOSTIC END === MetadataSource={MetadataSource}, " +
                "FinalOrderId={OrderId}, FinalUserId={UserId}",
                metadataSource,
                TryGetFromMetadata(metadata, "order_id") ?? "(null)",
                TryGetFromMetadata(metadata, "internal_user_id") ?? TryGetFromMetadata(metadata, "user_id") ?? "(null)");
            
            // Extract business data from metadata
            result.OrderId = TryGetFromMetadata(metadata, "order_id");
            result.ProductId = TryGetFromMetadata(metadata, "price_id");
            result.TransactionId = charge.Id;
            result.NewStatus = PaymentStatus.Refunded;
            result.ShouldProcess = true;
            
            // Extract UserId from metadata
            var userIdStr = TryGetFromMetadata(metadata, "internal_user_id") 
                         ?? TryGetFromMetadata(metadata, "user_id");
            if (Guid.TryParse(userIdStr, out var userId))
            {
                result.UserId = userId;
            }
            
            // Calculate refund amount (AmountRefunded is in cents)
            var refundAmount = charge.AmountRefunded / 100m;
            var originalAmount = charge.Amount / 100m;
            
            result.VerificationResult = new VerificationResult
            {
                IsValid = true,
                TransactionId = charge.Id,
                Amount = refundAmount,
                Currency = charge.Currency?.ToUpper() ?? "USD",
                ErrorMessage = charge.Refunded ? "Full refund" : "Partial refund"
            };
            
            _logger.LogInformation(
                "[StripeProvider] charge.refunded: OrderId={OrderId}, ChargeId={ChargeId}, UserId={UserId}, " +
                "ProductId={ProductId}, RefundAmount={RefundAmount}, OriginalAmount={OriginalAmount}, FullRefund={FullRefund}",
                result.OrderId, charge.Id, result.UserId, result.ProductId, refundAmount, originalAmount, charge.Refunded);
        }
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
    
    /// <summary>
    /// Extract value from Stripe metadata dictionary (same as old code)
    /// </summary>
    private static string? TryGetFromMetadata(IDictionary<string, string>? metadata, string key)
    {
        if (metadata != null && metadata.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }
        return null;
    }
    
    /// <summary>
    /// Extract string value from JSON element (for JSON fallback parsing)
    /// </summary>
    private static string? TryGetJsonString(System.Text.Json.JsonElement element, string key)
    {
        if (element.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
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
    
    /// <summary>
    /// Original plan type value from config (1=Day, 2=Month, 3=Year, 4=Week).
    /// This matches the legacy GodGPT PlanType enum values.
    /// </summary>
    public int PlanType { get; set; }
    
    public string Mode { get; set; } = "subscription";
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
