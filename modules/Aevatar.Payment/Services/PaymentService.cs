using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Agents.Protos;
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

    public PaymentService(
        IEnumerable<IPaymentProvider> providers,
        IGAgentActorFactory actorFactory,
        ILogger<PaymentService> logger)
    {
        _providers = providers;
        _actorFactory = actorFactory;
        _logger = logger;
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

        var status = new UserSubscriptionStatus
        {
            HasActiveSubscription = subscriptions.Any(),
            ActiveSubscriptions = subscriptions.Select(ToDto).ToList()
        };

        // Set primary subscription info from first active
        var primary = subscriptions.FirstOrDefault();
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
        // Note: In full implementation, this should query from CQRS read model (database)
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
            // - SubscriptionId: Creation returns sessionId (cs_test_xxx), webhook has real subscriptionId (sub_xxx) - VALUES DIFFERENT!
            // - OrderId: Created and stored in metadata during creation, extracted from metadata in webhook - VALUES SAME!
            // OrderId is guaranteed to exist because provider generates it if not provided
            var orderId = result.OrderId 
                ?? request.Metadata.GetValueOrDefault("order_id") 
                ?? throw new InvalidOperationException("OrderId is required to create payment record");
            var paymentId = GetPaymentId(platform, orderId);
            
            // Create payment record agent
            var recordAgent = await GetRecordAgentAsync(paymentId);
            
            await recordAgent.InitializeAsync(new AgentModels.Protos.CreatePaymentRequestProto
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
            });

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
                ProductName = request.ProductId,
                Amount = (long)((request.Metadata.TryGetValue("amount", out var amt) 
                    ? decimal.Parse(amt) : 0) * 100),
                Currency = "USD",
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
            // OrderId is extracted from subscription/invoice metadata and matches the key used when creating the record
            // OrderId is guaranteed to exist because provider generates it if not provided during creation
            // Unlike SubscriptionId which differs between creation (sessionId) and webhook (real subscriptionId)
            if (string.IsNullOrEmpty(result.OrderId))
            {
                _logger.LogWarning(
                    "[PaymentService] OrderId is empty, cannot find payment record. SubscriptionId={SubscriptionId}. " +
                    "This may indicate metadata was not properly set during subscription creation.",
                    result.SubscriptionId);
                return;
            }
            
            var paymentId = GetPaymentId(platform, result.OrderId);
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
                    paymentId, result.OrderId, result.SubscriptionId, result.UserId);
                
                // Initialize payment record from webhook data
                await recordAgent.InitializeAsync(new AgentModels.Protos.CreatePaymentRequestProto
                {
                    UserId = result.UserId.Value.ToString(),
                    Platform = (int)ToAgentPlatform(platform),
                    ExternalOrderId = result.OrderId,
                    SubscriptionId = result.SubscriptionId ?? string.Empty,
                    CustomerId = string.Empty, // Not available in webhook
                    ProductId = result.ProductId ?? string.Empty,
                    ProductName = result.ProductId ?? string.Empty,
                    PaymentMode = (int)AgentModels.PaymentMode.Subscription,
                    BusinessType = "godgpt",
                    BusinessId = result.ProductId ?? string.Empty
                });
            }

            // Get payment record state (Protobuf) for event context
            var recordState = await recordAgent.GetRecordStateAsync();
            
            // Update SubscriptionId if webhook provides one (real sub_xxx after checkout)
            if (!string.IsNullOrEmpty(result.SubscriptionId) && 
                recordState.SubscriptionId != result.SubscriptionId)
            {
                _logger.LogInformation(
                    "[PaymentService] Updating SubscriptionId for {PaymentId} from '{OldId}' to '{NewId}'",
                    paymentId, recordState.SubscriptionId, result.SubscriptionId);
                await recordAgent.UpdateSubscriptionIdAsync(result.SubscriptionId);
                // Refresh state after update
                recordState = await recordAgent.GetRecordStateAsync();
            }
            
            var eventContext = BuildEventContext(recordState, platform, paymentId);

            // Get index agent for event broadcasting (requires UserId)
            AgentModels.IPaymentIndexGAgent? indexAgent = null;
            if (result.UserId.HasValue)
            {
                indexAgent = await GetIndexAgentAsync(result.UserId.Value);
            }

            if (result.NewStatus.HasValue)
            {
                var agentStatus = ToAgentStatus(result.NewStatus.Value);
                
                if (result.NewStatus == PaymentStatus.Completed)
                {
                    var isRenewal = result.VerificationResult?.ExpiresDate != null && 
                                    recordState?.Status == (int)AgentModels.PaymentStatus.Completed;
                    
                    // Process renewal in agent
                    if (result.VerificationResult?.ExpiresDate != null)
                    {
                        await recordAgent.ProcessRenewalAsync(new AgentModels.RenewalInfo
                        {
                            ExternalTransactionId = result.TransactionId,
                            PeriodStart = DateTime.UtcNow,
                            PeriodEnd = result.VerificationResult.ExpiresDate.Value,
                            Amount = 0
                        });

                        if (indexAgent != null)
                        {
                            await indexAgent.UpdateSubscriptionPeriodEndAsync(
                                paymentId, result.VerificationResult.ExpiresDate.Value);
                        }
                    }

                    // Build payment completed event
                    var completedEvent = new PaymentCompletedEvent
                    {
                        Context = eventContext,
                        TransactionId = result.TransactionId ?? string.Empty,
                        PeriodStart = result.VerificationResult?.ExpiresDate != null
                            ? Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                            : null,
                        PeriodEnd = result.VerificationResult?.ExpiresDate != null
                            ? Timestamp.FromDateTime(result.VerificationResult.ExpiresDate.Value.ToUniversalTime())
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
                    // Update agent status (no business event - business layer tracks via period_end)
                    await recordAgent.UpdateStatusAsync(agentStatus);

                    if (indexAgent != null)
                    {
                        await indexAgent.RemoveActiveSubscriptionAsync(paymentId);
                    }
                }
                else if (result.NewStatus == PaymentStatus.Refunded)
                {
                    await recordAgent.UpdateStatusAsync(agentStatus);

                    // Build refund completed event
                    var refundEvent = new RefundCompletedEvent
                    {
                        Context = eventContext,
                        OriginalTransactionId = result.TransactionId ?? string.Empty,
                        RefundAmount = recordState?.Amount ?? 0,
                        Reason = "refund",
                        RefundType = "full",
                        RefundedAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
                    };

                    if (indexAgent != null)
                    {
                        await indexAgent.RemoveActiveSubscriptionAsync(paymentId);
                        
                        // Broadcast to business agents via IndexAgent
                        await indexAgent.NotifyRefundCompletedAsync(refundEvent);
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
            await recordAgent.CancelAsync(reason);
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
