using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using AgentModels = Aevatar.Payment.Agents;

namespace Aevatar.Payment.Services;

/// <summary>
/// Payment service orchestrator - routes requests to appropriate providers.
/// Uses PaymentIndexGAgent and PaymentRecordGAgent architecture.
/// Events are broadcast via PaymentIndexGAgent.NotifyXxxAsync (Down to children).
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<PaymentService> _logger;
    private readonly IStateIndexService? _stateIndexService;

    public PaymentService(
        IEnumerable<IPaymentProvider> providers,
        IGAgentActorFactory actorFactory,
        ILogger<PaymentService> logger,
        IStateIndexService? stateIndexService = null)
    {
        _providers = providers;
        _actorFactory = actorFactory;
        _logger = logger;
        _stateIndexService = stateIndexService;
    }

    private IPaymentProvider GetProvider(PaymentPlatform platform)
    {
        var provider = _providers.FirstOrDefault(p => p.Platform == platform);
        if (provider == null)
        {
            throw new NotSupportedException($"Payment platform {platform} is not supported");
        }
        return provider;
    }

    // ========== Product Operations ==========

    public async Task<List<ProductDto>> GetProductsAsync(
        PaymentPlatform platform, CancellationToken ct = default)
    {
        var provider = GetProvider(platform);
        return await provider.GetProductsAsync(ct);
    }

    // ========== Customer Operations (Stripe) ==========

    public async Task<CustomerSessionResult> GetStripeCustomerAsync(
        Guid userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[PaymentService] Getting Stripe customer for user {UserId}", userId);

        var provider = GetProvider(PaymentPlatform.Stripe);
        var indexAgent = await GetIndexAgentAsync(userId);

        // Check if we already have a customer ID stored
        var existingCustomerId = await indexAgent.GetPlatformCustomerIdAsync(
            AgentModels.PaymentPlatform.Stripe);

        string customerId;
        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            customerId = existingCustomerId;
            _logger.LogDebug("[PaymentService] Using existing customer {CustomerId} for user {UserId}",
                customerId, userId);
        }
        else
        {
            // Create new customer
            var customerResult = await provider.GetOrCreateCustomerAsync(userId, ct);
            if (!customerResult.Success || string.IsNullOrEmpty(customerResult.CustomerId))
            {
                return new CustomerSessionResult
                {
                    Success = false,
                    ErrorMessage = customerResult.ErrorMessage ?? "Failed to create customer"
                };
            }

            customerId = customerResult.CustomerId;

            // Store customer ID in index agent
            await indexAgent.SetPlatformCustomerIdAsync(
                AgentModels.PaymentPlatform.Stripe, customerId);

            _logger.LogInformation("[PaymentService] Created and stored customer {CustomerId} for user {UserId}",
                customerId, userId);
        }

        // Get customer session (EphemeralKey)
        return await provider.GetCustomerSessionAsync(userId, customerId, ct);
    }

    public async Task<PaymentSheetResult> CreatePaymentSheetAsync(
        Guid userId, PaymentSheetRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("[PaymentService] Creating PaymentSheet for user {UserId}", userId);

        var provider = GetProvider(PaymentPlatform.Stripe);

        // Ensure we have a customer ID
        if (string.IsNullOrEmpty(request.CustomerId))
        {
            var customerSession = await GetStripeCustomerAsync(userId, ct);
            if (!customerSession.Success || string.IsNullOrEmpty(customerSession.CustomerId))
            {
                return new PaymentSheetResult
                {
                    Success = false,
                    ErrorMessage = customerSession.ErrorMessage ?? "Failed to get customer"
                };
            }
            request.CustomerId = customerSession.CustomerId;
        }

        request.UserId = userId;
        return await provider.CreatePaymentSheetAsync(request, ct);
    }

    // ========== Subscription Operations ==========

    public async Task<SubscriptionResult> CreateSubscriptionAsync(
        Guid userId,
        PaymentPlatform platform,
        SubscriptionRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PaymentService] Creating subscription for user {UserId} on {Platform}",
            userId, platform);

        var provider = GetProvider(platform);
        request.UserId = userId;

        // For Stripe, ensure we have a customer ID if not provided
        if (platform == PaymentPlatform.Stripe && string.IsNullOrEmpty(request.CustomerId))
        {
            var indexAgent = await GetIndexAgentAsync(userId);
            var existingCustomerId = await indexAgent.GetPlatformCustomerIdAsync(
                AgentModels.PaymentPlatform.Stripe);
            
            if (!string.IsNullOrEmpty(existingCustomerId))
            {
                request.CustomerId = existingCustomerId;
            }
        }

        var result = await provider.CreateSubscriptionAsync(request, ct);

        // Use OrderId instead of SubscriptionId for consistency check
        // OrderId is guaranteed to exist (provider generates it if not provided)
        // SubscriptionId at creation time is sessionId, not real subscriptionId yet
        if (result.Success && !string.IsNullOrEmpty(result.OrderId))
        {
            await RecordPaymentAsync(userId, platform, request, result);
        }

        return result;
    }

    public async Task<CancellationResult> CancelSubscriptionAsync(
        Guid userId,
        PaymentPlatform platform,
        CancellationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PaymentService] Cancelling subscription {SubscriptionId} for user {UserId}",
            request.SubscriptionId, userId);

        var provider = GetProvider(platform);
        request.UserId = userId;

        var result = await provider.CancelSubscriptionAsync(request, ct);

        // Update PaymentRecordGAgent status after Stripe cancellation
        if (result.Success)
        {
            // If PaymentId not provided, lookup from PaymentIndexGAgent
            var paymentId = request.PaymentId;
            if (string.IsNullOrEmpty(paymentId))
            {
                var indexAgent = await GetIndexAgentAsync(userId);
                paymentId = await indexAgent.GetPaymentIdBySubscriptionIdAsync(request.SubscriptionId);
                
                if (string.IsNullOrEmpty(paymentId))
                {
                    _logger.LogWarning(
                        "[PaymentService] Could not find PaymentId for SubscriptionId {SubscriptionId}, user {UserId}",
                        request.SubscriptionId, userId);
                    return result;
                }
            }

            await CancelPaymentRecordAsync(paymentId, request.Reason);
        }

        return result;
    }

    // ========== Verification ==========

    public async Task<VerificationResult> VerifyTransactionAsync(
        Guid userId,
        PaymentPlatform platform,
        VerificationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PaymentService] Verifying transaction for user {UserId} on {Platform}",
            userId, platform);

        var provider = GetProvider(platform);
        request.UserId = userId;

        return await provider.VerifyTransactionAsync(request, ct);
    }

    // ========== Webhook Processing ==========

    public async Task<WebhookResult> HandleWebhookAsync(
        PaymentPlatform platform,
        WebhookRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[PaymentService] Handling webhook for {Platform}", platform);

        var provider = GetProvider(platform);
        var result = await provider.HandleWebhookAsync(request, ct);

        // Use OrderId instead of SubscriptionId for consistency check
        // OrderId is the stable key used for PaymentRecordGAgent lookup
        if (result.Success && result.ShouldProcess && !string.IsNullOrEmpty(result.OrderId))
        {
            await ProcessWebhookResultAsync(platform, result);
        }

        return result;
    }

    // ========== Query Operations ==========

    public async Task<UserSubscriptionStatus> GetUserSubscriptionStatusAsync(
        Guid userId, CancellationToken ct = default)
    {
        var indexAgent = await GetIndexAgentAsync(userId);
        var response = await indexAgent.GetActiveSubscriptionsAsync();
        var subscriptions = response.Subscriptions;

        // Filter out expired subscriptions (align with old code logic)
        var now = DateTime.UtcNow;
        var validSubscriptions = subscriptions
            .Where(s => s.PeriodEnd != null && s.PeriodEnd.ToDateTime() > now)
            .ToList();

        var status = new UserSubscriptionStatus
        {
            HasActiveSubscription = validSubscriptions.Any(),
            ActiveSubscriptions = validSubscriptions.Select(ToDto).ToList()
        };

        // Set primary subscription info from first active
        var primary = validSubscriptions.FirstOrDefault();
        if (primary != null)
        {
            status.CurrentPlan = primary.ProductName;
            status.Platform = ToApiPlatform((AgentModels.PaymentPlatform)primary.Platform);
            status.ExpiresAt = primary.PeriodEnd?.ToDateTime() ?? DateTime.MaxValue;
            status.SubscriptionId = primary.PaymentId;
            status.AutoRenew = primary.PeriodEnd?.ToDateTime() > DateTime.UtcNow;
        }

        return status;
    }

    public async Task<List<PaymentHistoryItem>> GetPaymentHistoryAsync(
        Guid userId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        // Use CQRS read model (Elasticsearch) if available, otherwise fallback to active subscriptions
        if (_stateIndexService != null)
        {
            try
            {
                // Query all records for the user (don't filter Processing in query)
                // We'll filter stale Processing records in C# like old code does
                var query = new StateQuery
                {
                    // Use full type name to match ES index: aevatar-state-aevatar-payment-agents-paymentrecordgagent
                    AgentType = "Aevatar.Payment.Agents.PaymentRecordGAgent",
                    // Elasticsearch fields are camelCase
                    // Use .keyword subfield for exact GUID match (text fields are analyzed by default)
                    QueryString = $"userId.keyword:\"{userId}\"",
                    PageIndex = 0, // Fetch more records for post-filtering
                    PageSize = pageSize * 3, // Fetch extra to account for filtered records
                    SortFields = new List<string> { "createdAt:desc" } // camelCase field name
                };

                var result = await _stateIndexService.QueryAsync(query, ct);
                
                _logger.LogInformation(
                    "[PaymentService] CQRS query returned {Count} payment records for user {UserId}",
                    result.Items.Count, userId);
                
                // Filter out stale Processing records (like old code)
                // Old code logic: Remove records where InvoiceDetails is empty AND Status == Processing AND CreatedAt > 1 day ago
                // In new code: Remove records where Transactions is empty AND Status == Processing AND CreatedAt > 1 day ago
                var oneDayAgo = DateTime.UtcNow.AddDays(-1);
                var processingStatus = (int)PaymentStatus.Processing;
                
                var filteredItems = result.Items
                    .Select(item => new { Item = item, State = ParseStateFromData(item.Data) })
                    .Where(x => 
                    {
                        // Keep all non-Processing records
                        if (x.State == null || x.State.Status != processingStatus)
                            return true;
                        
                        // For Processing records, only keep if:
                        // - Has transactions (InvoiceDetails equivalent), OR
                        // - Created within the last 1 day
                        var hasTransactions = x.State.Transactions != null && x.State.Transactions.Count > 0;
                        var isRecent = x.State.CreatedAt?.ToDateTime() > oneDayAgo;
                        return hasTransactions || isRecent;
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                
                return filteredItems.Select(x =>
                {
                    var state = x.State;
                    var item = x.Item;
                    
                    // Log first item's data keys for debugging
                    if (filteredItems.IndexOf(x) == 0 && item.Data.Any())
                    {
                        var keys = string.Join(", ", item.Data.Keys.Take(10));
                        _logger.LogDebug(
                            "[PaymentService] Sample data keys from CQRS: {Keys}",
                            keys);
                    }
                    
                    return new PaymentHistoryItem
                    {
                        PaymentId = state?.PaymentId ?? item.AgentId,
                        Platform = state != null 
                            ? ToApiPlatform((AgentModels.PaymentPlatform)state.Platform)
                            : PaymentPlatform.Stripe, // fallback
                        ProductName = state?.ProductName ?? string.Empty,
                        Amount = state != null ? state.Amount / 100m : 0,
                        Currency = state?.Currency ?? "USD",
                        Status = state != null ? (PaymentStatus)state.Status : PaymentStatus.Pending,
                        CreatedAt = state?.CreatedAt?.ToDateTime() ?? DateTime.UtcNow,
                        CompletedAt = state?.CompletedAt?.ToDateTime()
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "[PaymentService] Failed to query payment history from CQRS, falling back to active subscriptions");
            }
        }

        // Fallback: query active subscriptions only (limited history)
        var indexAgent = await GetIndexAgentAsync(userId);
        var response = await indexAgent.GetActiveSubscriptionsAsync();

        return response.Subscriptions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new PaymentHistoryItem
            {
                PaymentId = s.PaymentId,
                Platform = ToApiPlatform((AgentModels.PaymentPlatform)s.Platform),
                ProductName = s.ProductName,
                Amount = s.Amount / 100m,
                Currency = s.Currency,
                Status = PaymentStatus.Completed,
                CreatedAt = s.CreatedAt?.ToDateTime() ?? DateTime.UtcNow
            })
            .ToList();
    }

    private PaymentRecordStateProto? ParseStateFromData(Dictionary<string, object?> data)
    {
        try
        {
            // ElasticsearchStateIndexService stores fields in camelCase format
            // Extract key fields for PaymentHistoryItem mapping
            var proto = new PaymentRecordStateProto();
            
            // Try both camelCase and PascalCase (Protobuf C# properties are PascalCase)
            // StateDocumentConverter converts to camelCase, but check both for safety
            
            // Extract string fields (try camelCase first, then PascalCase)
            if (data.TryGetValue("paymentId", out var paymentId) && paymentId != null)
                proto.PaymentId = paymentId.ToString() ?? string.Empty;
            else if (data.TryGetValue("PaymentId", out paymentId) && paymentId != null)
                proto.PaymentId = paymentId.ToString() ?? string.Empty;
                
            if (data.TryGetValue("userId", out var userId) && userId != null)
                proto.UserId = userId.ToString() ?? string.Empty;
            else if (data.TryGetValue("UserId", out userId) && userId != null)
                proto.UserId = userId.ToString() ?? string.Empty;
                
            if (data.TryGetValue("productName", out var productName) && productName != null)
                proto.ProductName = productName.ToString() ?? string.Empty;
            else if (data.TryGetValue("ProductName", out productName) && productName != null)
                proto.ProductName = productName.ToString() ?? string.Empty;
                
            if (data.TryGetValue("currency", out var currency) && currency != null)
                proto.Currency = currency.ToString() ?? "USD";
            else if (data.TryGetValue("Currency", out currency) && currency != null)
                proto.Currency = currency.ToString() ?? "USD";
            
            // Extract numeric fields (try camelCase first, then PascalCase)
            if (data.TryGetValue("amount", out var amount) && amount != null)
            {
                if (amount is long amt)
                    proto.Amount = amt;
                else if (long.TryParse(amount.ToString(), out var parsedAmt))
                    proto.Amount = parsedAmt;
            }
            else if (data.TryGetValue("Amount", out amount) && amount != null)
            {
                if (amount is long amt)
                    proto.Amount = amt;
                else if (long.TryParse(amount.ToString(), out var parsedAmt))
                    proto.Amount = parsedAmt;
            }
            
            if (data.TryGetValue("status", out var status) && status != null)
            {
                if (status is int st)
                    proto.Status = st;
                else if (int.TryParse(status.ToString(), out var parsedSt))
                    proto.Status = parsedSt;
            }
            else if (data.TryGetValue("Status", out status) && status != null)
            {
                if (status is int st)
                    proto.Status = st;
                else if (int.TryParse(status.ToString(), out var parsedSt))
                    proto.Status = parsedSt;
            }
            
            if (data.TryGetValue("platform", out var platform) && platform != null)
            {
                if (platform is int plat)
                    proto.Platform = plat;
                else if (int.TryParse(platform.ToString(), out var parsedPlat))
                    proto.Platform = parsedPlat;
            }
            else if (data.TryGetValue("Platform", out platform) && platform != null)
            {
                if (platform is int plat)
                    proto.Platform = plat;
                else if (int.TryParse(platform.ToString(), out var parsedPlat))
                    proto.Platform = parsedPlat;
            }
            
            // Extract timestamp fields (try camelCase first, then PascalCase)
            if (data.TryGetValue("createdAt", out var createdAt) && createdAt != null)
            {
                if (createdAt is DateTime ct)
                    proto.CreatedAt = Timestamp.FromDateTime(ct.ToUniversalTime());
                else if (DateTime.TryParse(createdAt.ToString(), out var parsedCt))
                    proto.CreatedAt = Timestamp.FromDateTime(parsedCt.ToUniversalTime());
            }
            else if (data.TryGetValue("CreatedAt", out createdAt) && createdAt != null)
            {
                if (createdAt is DateTime ct)
                    proto.CreatedAt = Timestamp.FromDateTime(ct.ToUniversalTime());
                else if (DateTime.TryParse(createdAt.ToString(), out var parsedCt))
                    proto.CreatedAt = Timestamp.FromDateTime(parsedCt.ToUniversalTime());
            }
            
            if (data.TryGetValue("completedAt", out var completedAt) && completedAt != null)
            {
                if (completedAt is DateTime cat)
                    proto.CompletedAt = Timestamp.FromDateTime(cat.ToUniversalTime());
                else if (DateTime.TryParse(completedAt.ToString(), out var parsedCat))
                    proto.CompletedAt = Timestamp.FromDateTime(parsedCat.ToUniversalTime());
            }
            else if (data.TryGetValue("CompletedAt", out completedAt) && completedAt != null)
            {
                if (completedAt is DateTime cat)
                    proto.CompletedAt = Timestamp.FromDateTime(cat.ToUniversalTime());
                else if (DateTime.TryParse(completedAt.ToString(), out var parsedCat))
                    proto.CompletedAt = Timestamp.FromDateTime(parsedCat.ToUniversalTime());
            }
            
            return proto;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PaymentService] Failed to parse PaymentRecordStateProto from state data");
            return null;
        }
    }

    // ========== Private Helper Methods ==========

    private async Task<AgentModels.IPaymentIndexGAgent> GetIndexAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<AgentModels.PaymentIndexGAgent>(userId.ToString());
        return actor.As<AgentModels.IPaymentIndexGAgent>();
    }

    private async Task<AgentModels.IPaymentRecordGAgent> GetRecordAgentAsync(string paymentId)
    {
        // Convert paymentId to a stable Guid, then to string for agent ID
        var guidBytes = new byte[16];
        var hashBytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(paymentId));
        Array.Copy(hashBytes, guidBytes, 16);
        var agentId = new Guid(guidBytes);

        var actor = await _actorFactory.CreateGAgentActorAsync<AgentModels.PaymentRecordGAgent>(agentId.ToString());
        return actor.As<AgentModels.IPaymentRecordGAgent>();
    }

    private static string GetPaymentId(PaymentPlatform platform, string subscriptionId)
    {
        var platformName = platform switch
        {
            PaymentPlatform.Stripe => "stripe",
            PaymentPlatform.AppStore => "appstore",
            PaymentPlatform.GooglePlay => "googleplay",
            _ => "unknown"
        };
        return $"payment_{platformName}_{subscriptionId}";
    }

    private async Task RecordPaymentAsync(
        Guid userId,
        PaymentPlatform platform,
        SubscriptionRequest request,
        SubscriptionResult result)
    {
        try
        {
            // Use OrderId as stable key for PaymentRecordGAgent lookup
            // Key difference between OrderId and SubscriptionId:
            // - Stripe: OrderId is generated GUID stored in metadata, SubscriptionId differs between session (cs_xxx) and webhook (sub_xxx)
            // - Apple/Google: OrderId = SubscriptionId = OriginalTransactionId (stable identifier)
            // Use string.IsNullOrEmpty to handle both null and empty string cases
            var orderId = !string.IsNullOrEmpty(result.OrderId) 
                ? result.OrderId 
                : request.Metadata.GetValueOrDefault("order_id");
            
            // For Apple/Google, if OrderId is not available, use SubscriptionId (OriginalTransactionId) as fallback
            // This matches old code behavior where OrderId = SubscriptionId = OriginalTransactionId
            if (string.IsNullOrEmpty(orderId))
            {
                if (!string.IsNullOrEmpty(result.SubscriptionId))
                {
                    orderId = result.SubscriptionId;
                    _logger.LogInformation(
                        "[PaymentService] OrderId not found, using SubscriptionId as fallback (Apple/Google pattern): {OrderId}",
                        orderId);
                }
                else
                {
                    throw new InvalidOperationException("OrderId is required to create payment record. Neither OrderId nor SubscriptionId is available.");
                }
            }
            
            var paymentId = GetPaymentId(platform, orderId);
            
            // Create payment record agent
            var recordAgent = await GetRecordAgentAsync(paymentId);
            
            var createRequest = new AgentModels.Protos.CreatePaymentRequestProto
            {
                UserId = userId.ToString(),
                Platform = (int)ToAgentPlatform(platform),
                ExternalOrderId = orderId, // Store orderId for business logic reference
                SubscriptionId = string.Empty, // Will be set by webhook when real subscriptionId (sub_xxx) is available
                CustomerId = result.CustomerId ?? string.Empty,
                ProductId = request.ProductId ?? string.Empty,
                ProductName = request.ProductId ?? string.Empty,
                PaymentMode = (int)AgentModels.PaymentMode.Subscription,
                BusinessType = "godgpt",
                BusinessId = request.ProductId ?? string.Empty,
                PeriodEnd = result.ExpiresAt.HasValue 
                    ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(result.ExpiresAt.Value.ToUniversalTime())
                    : null
            };
            
            // Copy business metadata (plan_type, is_ultimate, etc.) for event broadcasting
            if (request.Metadata != null)
            {
                foreach (var kv in request.Metadata)
                {
                    createRequest.BusinessMetadata[kv.Key] = kv.Value;
                }
            }
            
            // Get product config for ProductName, Amount, and Currency (always fetch)
            string productName = request.ProductId ?? string.Empty;
            decimal productAmount = 0;
            string currency = "USD";
            
            try
            {
                var provider = GetProvider(platform);
                var products = await provider.GetProductsAsync();
                var product = products.FirstOrDefault(p => p.ProductId == request.ProductId);
                
                if (product != null)
                {
                    // Use product display name instead of ProductId (Price ID)
                    productName = product.Name ?? product.ProductId;
                    productAmount = product.Price;
                    currency = product.Currency ?? "USD";
                    
                    // Auto-infer plan_type and is_ultimate from product config if not provided
                    // Also set BillingCycle field using legacy PlanType values (Day=1, Month=2, Year=3, Week=4)
                    int legacyPlanType = 0;
                    if (!createRequest.BusinessMetadata.ContainsKey("plan_type"))
                    {
                        // Use originalPlanType from metadata (1=Day, 2=Month, 3=Year, 4=Week)
                        // This is the correct Common.Constants.PlanType value, not Payment.Abstractions.PlanType
                        if (product.Metadata != null && product.Metadata.TryGetValue("originalPlanType", out var originalPlanTypeStr))
                        {
                            createRequest.BusinessMetadata["plan_type"] = originalPlanTypeStr;
                            int.TryParse(originalPlanTypeStr, out legacyPlanType);
                        }
                        else
                        {
                            // Fallback: use product.PlanType (but this is wrong enum, should be avoided)
                            _logger.LogWarning(
                                "[PaymentService] originalPlanType not found in product metadata for {ProductId}, using fallback",
                                request.ProductId);
                            createRequest.BusinessMetadata["plan_type"] = ((int)product.PlanType).ToString();
                        }
                    }
                    else if (int.TryParse(createRequest.BusinessMetadata["plan_type"], out var pt))
                    {
                        legacyPlanType = pt;
                    }
                    
                    // Store BillingCycle as legacy PlanType value for consistent ES querying
                    if (legacyPlanType > 0)
                    {
                        createRequest.BillingCycle = legacyPlanType;
                    }
                    if (!createRequest.BusinessMetadata.ContainsKey("is_ultimate"))
                    {
                        // PlanType.Premium is used to indicate Ultimate tier in config
                        createRequest.BusinessMetadata["is_ultimate"] = (product.PlanType == PlanType.Premium).ToString().ToLower();
                    }
                    
                    var inferredPlanType = createRequest.BusinessMetadata.ContainsKey("plan_type") 
                        ? createRequest.BusinessMetadata["plan_type"] 
                        : "unknown";
                    _logger.LogInformation(
                        "[PaymentService] Fetched product config: name={ProductName}, amount={Amount}, currency={Currency}, plan_type={PlanType}, is_ultimate={IsUltimate}",
                        productName, productAmount, currency, inferredPlanType, product.PlanType == PlanType.Premium);
                }
                else
                {
                    _logger.LogWarning(
                        "[PaymentService] Product not found for ProductId={ProductId}, using defaults",
                        request.ProductId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PaymentService] Failed to fetch product config for ProductId={ProductId}", request.ProductId);
            }
            
            // Update ProductName, Amount, and Currency in createRequest
            createRequest.ProductName = productName;
            createRequest.Amount = (long)(productAmount * 100); // Convert to smallest unit (cents)
            createRequest.Currency = currency;
            
            await recordAgent.InitializeAsync(createRequest);

            // Update index agent
            var indexAgent = await GetIndexAgentAsync(userId);
            if (!string.IsNullOrEmpty(result.CustomerId))
            {
                await indexAgent.SetPlatformCustomerIdAsync(
                    ToAgentPlatform(platform), result.CustomerId);
            }
            await indexAgent.AddActiveSubscriptionAsync(new AgentModels.Protos.ActiveSubscriptionProto
            {
                PaymentId = paymentId,
                BusinessType = "godgpt",
                BusinessId = request.ProductId,
                Platform = (int)ToAgentPlatform(platform),
                ProductName = productName, // Use actual product name, not Price ID
                Amount = (long)(productAmount * 100), // Convert to smallest unit (cents)
                Currency = currency,
                SubscriptionId = result.SubscriptionId ?? string.Empty, // Store for cancellation lookup
                PeriodEnd = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
                    (result.ExpiresAt ?? DateTime.UtcNow.AddMonths(1)).ToUniversalTime()),
                CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            });
            await indexAgent.IncrementPaymentCountAsync();

            _logger.LogInformation(
                "[PaymentService] Recorded payment {PaymentId} for user {UserId}",
                paymentId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "[PaymentService] Failed to record payment for user {UserId}", userId);
        }
    }

    private async Task ProcessWebhookResultAsync(PaymentPlatform platform, WebhookResult result)
    {
        try
        {
            // Use OrderId as stable key for finding PaymentRecordGAgent
            // Key difference between OrderId and SubscriptionId:
            // - Stripe: OrderId is generated GUID stored in metadata, SubscriptionId differs between session (cs_xxx) and webhook (sub_xxx)
            // - Apple/Google: OrderId = SubscriptionId = OriginalTransactionId (stable identifier)
            var orderId = result.OrderId;
            
            // For Apple/Google, if OrderId is not available, use SubscriptionId (OriginalTransactionId) as fallback
            // This matches old code behavior where OrderId = SubscriptionId = OriginalTransactionId
            if (string.IsNullOrEmpty(orderId))
            {
                if (!string.IsNullOrEmpty(result.SubscriptionId))
                {
                    orderId = result.SubscriptionId;
                    _logger.LogInformation(
                        "[PaymentService] OrderId not found, using SubscriptionId as fallback (Apple/Google pattern): {OrderId}",
                        orderId);
                }
                else
                {
                    _logger.LogWarning(
                        "[PaymentService] Both OrderId and SubscriptionId are empty, cannot find payment record. " +
                        "This may indicate metadata was not properly set during subscription creation.");
                    return;
                }
            }
            
            var paymentId = GetPaymentId(platform, orderId);
            var recordAgent = await GetRecordAgentAsync(paymentId);

            var initialized = await recordAgent.IsInitializedAsync();
            if (!initialized)
            {
                // Like old code: if payment record not found, create a new one from webhook data
                // This handles cases where:
                // 1. Server restarted after checkout session creation but before persistence
                // 2. User completed payment through direct Stripe link
                // 3. Old subscriptions created before this code was deployed
                if (!result.UserId.HasValue)
                {
                    _logger.LogWarning(
                        "[PaymentService] Payment record {PaymentId} not found and UserId is missing, cannot create from webhook " +
                        "(OrderId={OrderId}, SubscriptionId={SubscriptionId})",
                        paymentId, result.OrderId, result.SubscriptionId);
                    return;
                }
                
                _logger.LogInformation(
                    "[PaymentService] Payment record {PaymentId} not found, creating from webhook data " +
                    "(OrderId={OrderId}, SubscriptionId={SubscriptionId}, UserId={UserId})",
                    paymentId, orderId, result.SubscriptionId, result.UserId);
                
                // Initialize payment record from webhook data
                var createFromWebhook = new AgentModels.Protos.CreatePaymentRequestProto
                {
                    UserId = result.UserId.Value.ToString(),
                    Platform = (int)ToAgentPlatform(platform),
                    ExternalOrderId = orderId, // Store orderId for business logic reference
                    SubscriptionId = result.SubscriptionId ?? string.Empty,
                    CustomerId = string.Empty, // Not available in webhook
                    ProductId = result.ProductId ?? string.Empty,
                    ProductName = result.ProductId ?? string.Empty, // Will be updated below
                    PaymentMode = (int)AgentModels.PaymentMode.Subscription,
                    BusinessType = "godgpt",
                    BusinessId = result.ProductId ?? string.Empty
                };
                
                // Get product config for ProductName and Amount
                string productName = result.ProductId ?? string.Empty;
                decimal productAmount = result.VerificationResult?.Amount ?? 0;
                string currency = result.VerificationResult?.Currency ?? "USD";
                
                // Auto-infer plan_type and is_ultimate from product config (like old code: GetProductConfigAsync)
                if (!string.IsNullOrEmpty(result.ProductId))
                {
                    try
                    {
                        var provider = GetProvider(platform);
                        var products = await provider.GetProductsAsync();
                        var product = products.FirstOrDefault(p => p.ProductId == result.ProductId);
                        
                        if (product != null)
                        {
                            // Use product display name instead of ProductId (Price ID)
                            productName = product.Name ?? product.ProductId;
                            // Use product price if verification result doesn't have amount
                            if (productAmount == 0)
                            {
                                productAmount = product.Price;
                            }
                            currency = product.Currency ?? currency;
                            
                            // Use originalPlanType from metadata (1=Day, 2=Month, 3=Year, 4=Week)
                            // This is the correct Common.Constants.PlanType value, not Payment.Abstractions.PlanType
                            int legacyPlanType = 0;
                            if (product.Metadata != null && product.Metadata.TryGetValue("originalPlanType", out var originalPlanTypeStr))
                            {
                                createFromWebhook.BusinessMetadata["plan_type"] = originalPlanTypeStr;
                                int.TryParse(originalPlanTypeStr, out legacyPlanType);
                            }
                            else
                            {
                                // Fallback: use product.PlanType (but this is wrong enum, should be avoided)
                                _logger.LogWarning(
                                    "[PaymentService] originalPlanType not found in product metadata for {ProductId}, using fallback",
                                    result.ProductId);
                                createFromWebhook.BusinessMetadata["plan_type"] = ((int)product.PlanType).ToString();
                            }
                            // PlanType.Premium is used to indicate Ultimate tier in config
                            createFromWebhook.BusinessMetadata["is_ultimate"] = (product.PlanType == PlanType.Premium).ToString().ToLower();
                            
                            // Store BillingCycle as legacy PlanType value for consistent ES querying
                            if (legacyPlanType > 0)
                            {
                                createFromWebhook.BillingCycle = legacyPlanType;
                            }
                            
                            var inferredPlanType = createFromWebhook.BusinessMetadata.ContainsKey("plan_type")
                                ? createFromWebhook.BusinessMetadata["plan_type"]
                                : "unknown";
                            _logger.LogInformation(
                                "[PaymentService] Inferred plan_type={PlanType}, is_ultimate={IsUltimate} for webhook record from product {ProductId}",
                                inferredPlanType, product.PlanType == PlanType.Premium, result.ProductId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[PaymentService] Product {ProductId} not found in config, using defaults for webhook record",
                                result.ProductId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[PaymentService] Failed to infer plan_type/is_ultimate from product config for webhook record");
                    }
                }
                
                // Update ProductName and Amount in createFromWebhook
                createFromWebhook.ProductName = productName;
                createFromWebhook.Amount = (long)(productAmount * 100); // Convert to smallest unit (cents)
                createFromWebhook.Currency = currency;
                
                await recordAgent.InitializeAsync(createFromWebhook);
            }

            // Get payment record state (Protobuf) for event context
            var recordState = await recordAgent.GetRecordStateAsync();
            
            // Get index agent once for both updates and event broadcasting (requires UserId)
            AgentModels.IPaymentIndexGAgent? indexAgent = null;
            if (result.UserId.HasValue)
            {
                indexAgent = await GetIndexAgentAsync(result.UserId.Value);
                
                // Add to index agent if payment record was just created and payment is completed
                if (!initialized && result.NewStatus == PaymentStatus.Completed)
                {
                    // Get product info again for index (already fetched above, but need to ensure we have it)
                    string indexProductName = result.ProductId ?? string.Empty;
                    decimal indexProductAmount = result.VerificationResult?.Amount ?? 0;
                    string indexCurrency = result.VerificationResult?.Currency ?? "USD";
                    
                    if (!string.IsNullOrEmpty(result.ProductId))
                    {
                        try
                        {
                            var provider = GetProvider(platform);
                            var products = await provider.GetProductsAsync();
                            var product = products.FirstOrDefault(p => p.ProductId == result.ProductId);
                            if (product != null)
                            {
                                indexProductName = product.Name ?? product.ProductId;
                                if (indexProductAmount == 0)
                                {
                                    indexProductAmount = product.Price;
                                }
                                indexCurrency = product.Currency ?? indexCurrency;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentService] Failed to get product info for index agent");
                        }
                    }
                    
                    await indexAgent.AddActiveSubscriptionAsync(new AgentModels.Protos.ActiveSubscriptionProto
                    {
                        PaymentId = paymentId,
                        BusinessType = "godgpt",
                        BusinessId = result.ProductId ?? string.Empty,
                        Platform = (int)ToAgentPlatform(platform),
                        ProductName = indexProductName, // Use actual product name, not Price ID
                        Amount = (long)(indexProductAmount * 100), // Convert to smallest unit (cents)
                        Currency = indexCurrency,
                        SubscriptionId = result.SubscriptionId ?? string.Empty,
                        PeriodEnd = result.PeriodEnd.HasValue
                            ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(result.PeriodEnd.Value.ToUniversalTime())
                            : (result.VerificationResult?.ExpiresDate.HasValue == true
                                ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(result.VerificationResult.ExpiresDate.Value.ToUniversalTime())
                                : Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddMonths(1))),
                        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                    });
                    await indexAgent.IncrementPaymentCountAsync();
                }
            }
            
            // Update SubscriptionId if webhook provides one (real sub_xxx after checkout)
            if (!string.IsNullOrEmpty(result.SubscriptionId) && 
                recordState.SubscriptionId != result.SubscriptionId)
            {
                _logger.LogInformation(
                    "[PaymentService] Updating SubscriptionId for {PaymentId} from '{OldId}' to '{NewId}'",
                    paymentId, recordState.SubscriptionId, result.SubscriptionId);
                await recordAgent.UpdateSubscriptionIdAsync(result.SubscriptionId);
                
                // Also update PaymentIndexGAgent for cancellation lookup
                if (indexAgent != null)
                {
                    await indexAgent.UpdateSubscriptionIdAsync(paymentId, result.SubscriptionId);
                }
                
                // Refresh state after update
                recordState = await recordAgent.GetRecordStateAsync();
            }
            
            // Update PeriodEnd if webhook provides one (invoice.paid renewals)
            if (indexAgent != null && result.PeriodEnd.HasValue)
            {
                _logger.LogInformation(
                    "[PaymentService] Updating PeriodEnd for {PaymentId} to {PeriodEnd}",
                    paymentId, result.PeriodEnd.Value);
                await indexAgent.UpdateSubscriptionPeriodEndAsync(paymentId, result.PeriodEnd.Value);
            }
            
            var eventContext = BuildEventContext(recordState, platform, paymentId);

            if (result.NewStatus.HasValue)
            {
                var agentStatus = ToAgentStatus(result.NewStatus.Value);
                
                if (result.NewStatus == PaymentStatus.Completed)
                {
                    // Idempotency check: don't complete if already cancelled/expired/refunded
                    var currentStatus = (PaymentStatus)recordState.Status;
                    if (currentStatus == PaymentStatus.Cancelled || 
                        currentStatus == PaymentStatus.Expired ||
                        currentStatus == PaymentStatus.Refunded)
                    {
                        _logger.LogWarning(
                            "[PaymentService] Payment {PaymentId} is {Status}, skipping Complete from webhook (possible race condition)",
                            paymentId, currentStatus);
                        return;
                    }
                    
                    var isRenewal = result.VerificationResult?.ExpiresDate != null && 
                                    recordState?.Status == (int)AgentModels.PaymentStatus.Completed;
                    
                    // CRITICAL: Update record status to Completed
                    // This triggers Event Sourcing and ES projection
                    if (!isRenewal)
                    {
                        await recordAgent.CompleteAsync();
                        _logger.LogInformation(
                            "[PaymentService] Marked payment {PaymentId} as Completed", paymentId);
                    }
                    
                    // Process renewal in agent
                    if (result.VerificationResult?.ExpiresDate != null)
                    {
                        // Convert amount to smallest unit (cents) for Protobuf
                        var renewalAmount = (long)((result.VerificationResult.Amount ?? 0) * 100);
                        
                        await recordAgent.ProcessRenewalAsync(new RenewalInfoProto
                        {
                            ExternalTransactionId = result.TransactionId ?? string.Empty,
                            PeriodStart = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime()),
                            PeriodEnd = Timestamp.FromDateTime(result.VerificationResult.ExpiresDate.Value.ToUniversalTime()),
                            Amount = renewalAmount,
                            Currency = result.VerificationResult.Currency ?? "USD"
                        });

                        if (indexAgent != null)
                        {
                            await indexAgent.UpdateSubscriptionPeriodEndAsync(
                                paymentId, result.VerificationResult.ExpiresDate.Value);
                        }
                    }

                    // Build payment completed event
                    // Use PeriodEnd from webhook result (invoice.paid) if available, otherwise fallback to VerificationResult
                    var periodEnd = result.PeriodEnd ?? result.VerificationResult?.ExpiresDate;
                    var completedEvent = new PaymentCompletedEvent
                    {
                        Context = eventContext,
                        TransactionId = result.TransactionId ?? string.Empty,
                        InvoiceId = result.TransactionId ?? string.Empty, // For Stripe, invoice ID is same as transaction ID
                        PeriodStart = periodEnd != null
                            ? Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                            : null,
                        PeriodEnd = periodEnd != null
                            ? Timestamp.FromDateTime(periodEnd.Value.ToUniversalTime())
                            : null,
                        IsRenewal = isRenewal,
                        CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                    };

                    // Broadcast to business agents via IndexAgent (user-level stream)
                    if (indexAgent != null)
                    {
                        await indexAgent.NotifyPaymentCompletedAsync(completedEvent);
                    }

                    // Point-to-point callback to order-level agent (if configured)
                    await recordAgent.NotifyCallbackAgentAsync(completedEvent);
                }
                else if (result.NewStatus == PaymentStatus.Cancelled || 
                         result.NewStatus == PaymentStatus.Expired)
                {
                    // Idempotency check: skip if already cancelled (e.g., user API cancel + webhook)
                    var currentStatus = (PaymentStatus)recordState.Status;
                    if (currentStatus == PaymentStatus.Cancelled || currentStatus == PaymentStatus.Expired)
                    {
                        _logger.LogInformation(
                            "[PaymentService] Payment {PaymentId} already {Status}, skipping duplicate cancellation from webhook",
                            paymentId, currentStatus);
                        return;
                    }
                    
                    // Cancel the payment record (triggers Event Sourcing)
                    await recordAgent.CancelAsync(result.VerificationResult?.ErrorMessage ?? "Subscription cancelled");

                    // Remove from active subscriptions index
                    if (indexAgent != null)
                    {
                        await indexAgent.RemoveActiveSubscriptionAsync(paymentId);
                        
                        // Build and broadcast cancellation event to business layer
                        var cancelledEvent = new PaymentCancelledEvent
                        {
                            Context = eventContext,
                            Reason = result.VerificationResult?.ErrorMessage ?? "Subscription cancelled",
                            Immediate = result.NewStatus == PaymentStatus.Cancelled, // Cancelled is immediate, Expired is at period end
                            EffectiveDate = result.PeriodEnd.HasValue
                                ? Timestamp.FromDateTime(result.PeriodEnd.Value.ToUniversalTime())
                                : null,
                            CancelledAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                        };
                        
                        await indexAgent.NotifyPaymentCancelledAsync(cancelledEvent);
                    }
                }
                else if (result.NewStatus == PaymentStatus.Refunded)
                {
                    // Process refund: updates transaction status and main payment status
                    // ProcessRefundAsync will set status to Refunded if all transactions refunded, or PartialRefunded otherwise
                    await recordAgent.ProcessRefundAsync(new RefundInfoProto
                    {
                        TransactionId = result.TransactionId ?? string.Empty, // If empty, refunds latest completed transaction
                        RefundAmount = recordState?.Amount ?? 0,
                        Reason = result.VerificationResult?.ErrorMessage ?? "refund"
                    });
                    
                    _logger.LogInformation(
                        "[PaymentService] Processed refund for payment {PaymentId}, transaction {TransactionId}",
                        paymentId, result.TransactionId ?? "latest");

                    // Build refund completed event (for Analytics reporting)
                    var refundEvent = new RefundCompletedEvent
                    {
                        Context = eventContext,
                        OriginalTransactionId = result.TransactionId ?? string.Empty,
                        RefundAmount = recordState?.Amount ?? 0,
                        Reason = "refund",
                        RefundType = "full",
                        RefundedAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                    };

                    // Build cancellation event (for business agents like UserQuotaGAgent)
                    // Refund should trigger same business logic as cancellation
                    var cancelledEvent = new PaymentCancelledEvent
                    {
                        Context = eventContext,
                        Reason = "refund",
                        Immediate = true, // Refunds are immediate
                        CancelledAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                    };

                    if (indexAgent != null)
                    {
                        await indexAgent.RemoveActiveSubscriptionAsync(paymentId);
                        
                        // Broadcast RefundCompletedEvent for Analytics (GA4 reporting)
                        await indexAgent.NotifyRefundCompletedAsync(refundEvent);
                        
                        // Also broadcast PaymentCancelledEvent for business agents (UserQuotaGAgent)
                        // This ensures consistent handling - refund triggers same logic as cancel
                        await indexAgent.NotifyPaymentCancelledAsync(cancelledEvent);
                    }

                    // Point-to-point callback to order-level agent (if configured)
                    await recordAgent.NotifyCallbackAgentAsync(refundEvent);
                }
                else if (result.NewStatus == PaymentStatus.Failed)
                {
                    await recordAgent.UpdateStatusAsync(agentStatus);

                    // Build payment failed event
                    var failedEvent = new PaymentFailedEvent
                    {
                        Context = eventContext,
                        ErrorCode = "payment_failed",
                        ErrorMessage = result.VerificationResult?.ErrorMessage ?? "Payment failed",
                        FailedAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                    };

                    // Broadcast to business agents via IndexAgent
                    if (indexAgent != null)
                    {
                        await indexAgent.NotifyPaymentFailedAsync(failedEvent);
                    }

                    // Point-to-point callback to order-level agent (if configured)
                    await recordAgent.NotifyCallbackAgentAsync(failedEvent);
                }
                else
                {
                    await recordAgent.UpdateStatusAsync(agentStatus);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "[PaymentService] Failed to process webhook for subscription {SubscriptionId}",
                result.SubscriptionId);
        }
    }

    private static PaymentEventContext BuildEventContext(
        AgentModels.Protos.PaymentRecordStateProto? recordState,
        PaymentPlatform platform,
        string paymentId)
    {
        var context = new PaymentEventContext
        {
            PaymentId = paymentId,
            Platform = (int)platform
        };

        if (recordState != null)
        {
            context.UserId = recordState.UserId;
            context.SubscriptionId = recordState.SubscriptionId;
            context.CustomerId = recordState.CustomerId;
            context.BusinessType = recordState.BusinessType;
            context.BusinessId = recordState.BusinessId;
            context.Environment = recordState.Environment;
            context.ProductId = recordState.ProductId;
            context.ProductName = recordState.ProductName;
            context.PaymentMode = recordState.PaymentMode;
            context.Amount = recordState.Amount;
            context.Currency = recordState.Currency;

            if (recordState.BusinessMetadata != null)
            {
                foreach (var kv in recordState.BusinessMetadata)
                {
                    context.BusinessMetadata[kv.Key] = kv.Value;
                }
            }
        }

        return context;
    }

    private async Task CancelPaymentRecordAsync(string paymentId, string? reason)
    {
        try
        {
            var recordAgent = await GetRecordAgentAsync(paymentId);
            
            // Get record state to extract UserId for index agent and build event context
            var recordState = await recordAgent.GetRecordStateAsync();
            
            // Stage 1: Set status to CancelledInProcessing (7) - matches old code behavior
            // Final Cancelled (8) will be set when webhook confirms the cancellation
            await recordAgent.UpdateStatusAsync(
                AgentModels.PaymentStatus.CancelledInProcessing, 
                reason ?? "User requested cancellation");
            
            // Remove from PaymentIndexGAgent's active subscriptions and notify business layer
            if (recordState != null && !string.IsNullOrEmpty(recordState.UserId))
            {
                try
                {
                    var userId = Guid.Parse(recordState.UserId);
                    var indexAgent = await GetIndexAgentAsync(userId);
                    await indexAgent.RemoveActiveSubscriptionAsync(paymentId);
                    
                    // Build event context for cancellation event
                    var eventContext = BuildEventContext(recordState, 
                        ToApiPlatform((AgentModels.PaymentPlatform)recordState.Platform), 
                        paymentId);
                    
                    // Build and broadcast cancellation event to business layer
                    var cancelledEvent = new PaymentCancelledEvent
                    {
                        Context = eventContext,
                        Reason = reason ?? "User requested cancellation",
                        Immediate = true, // Active cancellation is immediate
                        CancelledAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                    };
                    
                    await indexAgent.NotifyPaymentCancelledAsync(cancelledEvent);
                    
                    _logger.LogInformation(
                        "[PaymentService] Cancelled subscription {PaymentId} for user {UserId} and notified business layer",
                        paymentId, userId);
                }
                catch (Exception indexEx)
                {
                    _logger.LogError(indexEx,
                        "[PaymentService] Failed to remove subscription {PaymentId} from index or notify business layer", paymentId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "[PaymentService] Failed to cancel payment record {PaymentId}", paymentId);
        }
    }

    // ========== Type Conversions ==========

    private static AgentModels.PaymentPlatform ToAgentPlatform(PaymentPlatform platform)
    {
        return (AgentModels.PaymentPlatform)(int)platform;
    }

    private static PaymentPlatform ToApiPlatform(AgentModels.PaymentPlatform platform)
    {
        return (PaymentPlatform)(int)platform;
    }

    private static AgentModels.PaymentStatus ToAgentStatus(PaymentStatus status)
    {
        return (AgentModels.PaymentStatus)(int)status;
    }

    private static ActiveSubscriptionDto ToDto(AgentModels.Protos.ActiveSubscriptionProto sub)
    {
        return new ActiveSubscriptionDto
        {
            PaymentId = sub.PaymentId,
            BusinessType = sub.BusinessType,
            BusinessId = sub.BusinessId,
            Platform = ToApiPlatform((AgentModels.PaymentPlatform)sub.Platform),
            ProductName = sub.ProductName,
            Amount = sub.Amount / 100m,
            Currency = sub.Currency,
            PeriodEnd = sub.PeriodEnd?.ToDateTime() ?? DateTime.MaxValue,
            CreatedAt = sub.CreatedAt?.ToDateTime() ?? DateTime.UtcNow
        };
    }
}
