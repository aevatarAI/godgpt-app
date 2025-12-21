using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.GodGPT.Dtos;

namespace Aevatar.App.Application.Services.Payment;

/// <summary>
/// Service interface for managing payment operations.
/// Handles Stripe, Apple, and Google Play payments.
/// </summary>
public interface IGodGPTPaymentService
{
    /// <summary>
    /// Gets the list of Stripe products.
    /// </summary>
    Task<List<StripeProductDto>> GetStripeProductsAsync(Guid currentUserId);

    /// <summary>
    /// Gets the list of Apple products.
    /// </summary>
    Task<List<AppleProductDto>> GetAppleProductsAsync(Guid currentUserId);

    /// <summary>
    /// Creates a checkout session.
    /// </summary>
    Task<string> CreateCheckoutSessionAsync(Guid currentUserId, CreateCheckoutSessionInput input);

    /// <summary>
    /// Gets payment history for a user.
    /// </summary>
    Task<List<PaymentSummaryDto>> GetPaymentHistoryAsync(Guid currentUserId, GetPaymentHistoryInput input);

    /// <summary>
    /// Gets Stripe customer information.
    /// </summary>
    Task<GetCustomerResponseDto> GetStripeCustomerAsync(Guid currentUserId);

    /// <summary>
    /// Creates a subscription.
    /// </summary>
    Task<SubscriptionResponseDto> CreateSubscriptionAsync(Guid currentUserId, CreateSubscriptionInput input);

    /// <summary>
    /// Cancels a subscription.
    /// </summary>
    Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(Guid currentUserId, CancelSubscriptionInput input);

    /// <summary>
    /// Verifies an App Store receipt.
    /// </summary>
    Task<AppStoreSubscriptionResponseDto> VerifyAppStoreReceiptAsync(Guid currentUserId, VerifyAppStoreReceiptInput input);

    /// <summary>
    /// Verifies a Google Play transaction.
    /// </summary>
    Task<PaymentVerificationResponseDto> VerifyGooglePlayTransactionAsync(Guid currentUserId, GooglePlayTransactionVerificationRequestDto input);
}
