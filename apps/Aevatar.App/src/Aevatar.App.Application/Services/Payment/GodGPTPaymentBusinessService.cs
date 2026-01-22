using System;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Agents;
using Aevatar.Payment.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Alias to resolve naming conflict between Abstractions and Agents
using PaymentPlatform = Aevatar.Payment.Abstractions.PaymentPlatform;

namespace Aevatar.App.Application.Services.Payment;

/// <summary>
/// GodGPT-specific payment business logic service.
/// 
/// Handles side effects that require external API calls (Stripe) which are only
/// available in HttpApi.Host layer (not in Silo).
/// 
/// Architecture:
/// - PaymentService (module): Generic payment operations, publishes events
/// - This Service (application): GodGPT-specific business logic (cancel old subscriptions)
/// - Agent (silo): State management (quota, subscription status)
/// </summary>
public interface IGodGPTPaymentBusinessService
{
    /// <summary>
    /// Process side effects after a successful payment webhook.
    /// Cancels old subscriptions to prevent double billing.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="platform">Payment platform (Stripe, Apple, Google)</param>
    /// <param name="newSubscriptionId">New subscription ID</param>
    /// <param name="productId">Product/Price ID for determining subscription tier</param>
    /// <param name="isRenewal">Whether this is a renewal payment</param>
    Task HandlePaymentSuccessAsync(
        Guid userId,
        PaymentPlatform platform,
        string newSubscriptionId,
        string? productId,
        bool isRenewal);
}

public class GodGPTPaymentBusinessService : IGodGPTPaymentBusinessService
{
    private readonly IPaymentService _paymentService;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly PaymentBusinessRegistrationService _registrationService;
    private readonly ILogger<GodGPTPaymentBusinessService> _logger;
    private readonly StripeOptions _stripeOptions;
    private readonly ApplePayOptions _applePayOptions;
    private readonly GooglePlayOptions _googlePlayOptions;

    public GodGPTPaymentBusinessService(
        IPaymentService paymentService,
        IGAgentActorFactory actorFactory,
        PaymentBusinessRegistrationService registrationService,
        ILogger<GodGPTPaymentBusinessService> logger,
        IOptions<StripeOptions> stripeOptions,
        IOptions<ApplePayOptions> applePayOptions,
        IOptions<GooglePlayOptions> googlePlayOptions)
    {
        _paymentService = paymentService;
        _actorFactory = actorFactory;
        _registrationService = registrationService;
        _logger = logger;
        _stripeOptions = stripeOptions.Value;
        _applePayOptions = applePayOptions.Value;
        _googlePlayOptions = googlePlayOptions.Value;
    }

    public async Task HandlePaymentSuccessAsync(
        Guid userId,
        PaymentPlatform platform,
        string newSubscriptionId,
        string? productId,
        bool isRenewal)
    {
        _logger.LogInformation(
            "[GodGPTPaymentBusinessService] === PAYMENT SUCCESS CALLBACK START === " +
            "UserId={UserId}, Platform={Platform}, SubscriptionId={SubscriptionId}, ProductId={ProductId}, IsRenewal={IsRenewal}",
            userId, platform, newSubscriptionId, productId, isRenewal);
        
        // Ensure business agents are linked to PaymentIndexGAgent
        // This enables them to receive PaymentCompletedEvent via event broadcasting
        try
        {
            await _registrationService.RegisterBusinessAgentsForUserAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GodGPTPaymentBusinessService] Failed to register business agents for user {UserId}, continuing anyway",
                userId);
        }
        
        // Renewals don't need to cancel old subscriptions
        if (isRenewal)
        {
            _logger.LogInformation(
                "[GodGPTPaymentBusinessService] Skipping renewal - no need to cancel old subscriptions. UserId={UserId}",
                userId);
            return;
        }

        // Determine isUltimate from product configuration
        var isUltimate = GetIsUltimateFromProductId(platform, productId);

        _logger.LogInformation(
            "[GodGPTPaymentBusinessService] Product lookup result: ProductId={ProductId}, IsUltimate={IsUltimate}",
            productId, isUltimate);

        try
        {
            await CancelOldSubscriptionsAsync(userId, platform, newSubscriptionId, isUltimate);
        }
        catch (Exception ex)
        {
            // Log but don't fail - this is a side effect
            // Manual intervention may be needed if this fails
            _logger.LogError(ex,
                "[GodGPTPaymentBusinessService] Failed to cancel old subscriptions for user {UserId}",
                userId);
        }
        
        _logger.LogInformation("[GodGPTPaymentBusinessService] === PAYMENT SUCCESS CALLBACK END === UserId={UserId}", userId);
    }

    /// <summary>
    /// Cancel old subscriptions based on upgrade rules:
    /// - Same type (Basic→Basic, Ultimate→Ultimate): Cancel old subscription
    /// - Upgrade (Basic→Ultimate): Cancel all Basic subscriptions
    /// </summary>
    private async Task CancelOldSubscriptionsAsync(
        Guid userId,
        PaymentPlatform platform,
        string newSubscriptionId,
        bool isUltimate)
    {
        _logger.LogInformation(
            "[GodGPTPaymentBusinessService] Fetching active subscriptions for user {UserId}...",
            userId);
        
        // Get active subscriptions from PaymentIndexGAgent
        var paymentIndexActor = await _actorFactory.CreateGAgentActorAsync<PaymentIndexGAgent>(userId.ToString());
        var paymentIndexAgent = paymentIndexActor.As<IPaymentIndexGAgent>();
        var subscriptions = await paymentIndexAgent.GetActiveSubscriptionsByBusinessAsync("godgpt");

        _logger.LogInformation(
            "[GodGPTPaymentBusinessService] Found {Count} active godgpt subscriptions for user {UserId}",
            subscriptions.Subscriptions.Count, userId);

        var cancelledCount = 0;

        foreach (var sub in subscriptions.Subscriptions)
        {
            _logger.LogDebug(
                "[GodGPTPaymentBusinessService] Checking subscription: PaymentId={PaymentId}, Platform={Platform}, BusinessId={BusinessId}",
                sub.PaymentId, sub.Platform, sub.BusinessId);
            
            // Skip the new subscription itself
            if (sub.PaymentId.Contains(newSubscriptionId))
            {
                _logger.LogDebug(
                    "[GodGPTPaymentBusinessService] Skipping new subscription: {PaymentId}",
                    sub.PaymentId);
                continue;
            }

            // Get subscription's platform and productId to determine isUltimate
            var subPlatform = (PaymentPlatform)sub.Platform;
            var subProductId = sub.BusinessId; // BusinessId stores productId
            var subIsUltimate = GetIsUltimateFromStoredPayment(subPlatform, subProductId);
            var subscriptionId = ExtractSubscriptionIdFromPaymentId(sub.PaymentId);

            _logger.LogInformation(
                "[GodGPTPaymentBusinessService] Old subscription: PaymentId={PaymentId}, SubId={SubscriptionId}, Platform={Platform}, ProductId={ProductId}, IsUltimate={IsUltimate}",
                sub.PaymentId, subscriptionId, subPlatform, subProductId, subIsUltimate);

            if (string.IsNullOrEmpty(subscriptionId))
            {
                _logger.LogWarning(
                    "[GodGPTPaymentBusinessService] Could not extract subscription ID from PaymentId={PaymentId}",
                    sub.PaymentId);
                continue;
            }

            // Determine if we should cancel this subscription
            bool shouldCancel = false;
            string reason = string.Empty;

            if (isUltimate && !subIsUltimate)
            {
                // Ultimate upgrade: cancel all Basic subscriptions
                shouldCancel = true;
                reason = $"Upgrade to Ultimate subscription {newSubscriptionId}";
                _logger.LogInformation(
                    "[GodGPTPaymentBusinessService] Decision: CANCEL (Ultimate upgrade, cancelling Basic)");
            }
            else if (subIsUltimate == isUltimate)
            {
                // Same type: cancel old subscriptions
                shouldCancel = true;
                reason = $"Replaced by new subscription {newSubscriptionId}";
                _logger.LogInformation(
                    "[GodGPTPaymentBusinessService] Decision: CANCEL (Same tier replacement)");
            }
            else
            {
                _logger.LogInformation(
                    "[GodGPTPaymentBusinessService] Decision: KEEP (New={NewIsUltimate}, Old={OldIsUltimate})",
                    isUltimate, subIsUltimate);
            }

            if (shouldCancel)
            {
                _logger.LogInformation(
                    "[GodGPTPaymentBusinessService] Cancelling subscription {SubscriptionId} (Platform={Platform}) for user {UserId}, reason: {Reason}",
                    subscriptionId, subPlatform, userId, reason);

                try
                {
                    // Use subscription's own platform, not the new subscription's platform
                    await _paymentService.CancelSubscriptionAsync(userId, subPlatform, new CancellationRequest
                    {
                        SubscriptionId = subscriptionId,
                        Reason = reason,
                        Immediate = false // Cancel at period end
                    });
                    cancelledCount++;
                    _logger.LogInformation(
                        "[GodGPTPaymentBusinessService] Successfully cancelled subscription {SubscriptionId}",
                        subscriptionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[GodGPTPaymentBusinessService] Failed to cancel subscription {SubscriptionId}",
                        subscriptionId);
                }
            }
        }

        _logger.LogInformation(
            "[GodGPTPaymentBusinessService] Cancelled {Count} old subscriptions for user {UserId}",
            cancelledCount, userId);
    }

    /// <summary>
    /// Determine if subscription is Ultimate tier from product configuration.
    /// </summary>
    private bool GetIsUltimateFromProductId(PaymentPlatform platform, string? productId)
    {
        if (string.IsNullOrEmpty(productId))
        {
            _logger.LogWarning("[GodGPTPaymentBusinessService] ProductId is null, defaulting isUltimate=false");
            return false;
        }

        return platform switch
        {
            PaymentPlatform.Stripe => _stripeOptions.Products
                .FirstOrDefault(p => p.PriceId == productId)?.IsUltimate ?? false,
            PaymentPlatform.AppStore => _applePayOptions.Products
                .FirstOrDefault(p => p.ProductId == productId)?.IsUltimate ?? false,
            PaymentPlatform.GooglePlay => _googlePlayOptions.Products
                .FirstOrDefault(p => p.ProductId == productId)?.IsUltimate ?? false,
            _ => false
        };
    }

    /// <summary>
    /// Get isUltimate status for an existing subscription from its stored productId.
    /// Used when cancelling old subscriptions.
    /// </summary>
    private bool GetIsUltimateFromStoredPayment(PaymentPlatform platform, string? productId)
    {
        // Reuse the same logic
        return GetIsUltimateFromProductId(platform, productId);
    }

    private static string? ExtractSubscriptionIdFromPaymentId(string paymentId)
    {
        // Payment ID format: payment_{platform}_{subscriptionId}
        // e.g., "payment_stripe_sub_1234" -> "sub_1234"
        // SubscriptionId may contain underscores, so we skip first two parts
        var parts = paymentId.Split('_');
        if (parts.Length < 3)
        {
            return null;
        }
        // Skip "payment" and platform name, join the rest
        return string.Join("_", parts.Skip(2));
    }
}
