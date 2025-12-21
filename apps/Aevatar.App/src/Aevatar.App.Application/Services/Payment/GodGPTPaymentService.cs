using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.GodGPT.Dtos;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace Aevatar.App.Application.Services.Payment;

/// <summary>
/// Service implementation for managing payment operations.
/// Handles Stripe, Apple, and Google Play payments through UserBillingGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTPaymentService : ApplicationService, IGodGPTPaymentService
{
    private readonly IGAgentFactory _agentFactory;
    private readonly ILogger<GodGPTPaymentService> _logger;

    public GodGPTPaymentService(
        IGAgentFactory agentFactory,
        ILogger<GodGPTPaymentService> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<StripeProductDto>> GetStripeProductsAsync(Guid currentUserId)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        return await userBillingGAgent.GetStripeProductsAsync();
    }

    /// <inheritdoc />
    public async Task<List<AppleProductDto>> GetAppleProductsAsync(Guid currentUserId)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        return await userBillingGAgent.GetAppleProductsAsync();
    }

    /// <inheritdoc />
    public async Task<string> CreateCheckoutSessionAsync(Guid currentUserId, CreateCheckoutSessionInput input)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        var result = await userBillingGAgent.CreateCheckoutSessionAsync(new CreateCheckoutSessionDto
        {
            UserId = currentUserId.ToString(),
            PriceId = input.PriceId,
            Mode = input.Mode ?? PaymentMode.SUBSCRIPTION,
            Quantity = input.Quantity <= 0 ? 1 : input.Quantity,
            UiMode = input.UiMode ?? StripeUiMode.HOSTED,
            CancelUrl = input.CancelUrl
        });
        return result;
    }

    /// <inheritdoc />
    public async Task<List<PaymentSummaryDto>> GetPaymentHistoryAsync(Guid currentUserId, GetPaymentHistoryInput input)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        return await userBillingGAgent.GetPaymentHistoryAsync(input.Page, input.PageSize);
    }

    /// <inheritdoc />
    public async Task<GetCustomerResponseDto> GetStripeCustomerAsync(Guid currentUserId)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        return await userBillingGAgent.GetStripeCustomerAsync(currentUserId.ToString());
    }

    /// <inheritdoc />
    public async Task<SubscriptionResponseDto> CreateSubscriptionAsync(Guid currentUserId, CreateSubscriptionInput input)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        return await userBillingGAgent.CreateSubscriptionAsync(new CreateSubscriptionDto
        {
            UserId = currentUserId,
            PriceId = input.PriceId,
            Quantity = input.Quantity,
            PaymentMethodId = input.PaymentMethodId,
            Description = input.Description,
            Metadata = input.Metadata,
            TrialPeriodDays = input.TrialPeriodDays,
            Platform = input.DevicePlatform
        });
    }

    /// <inheritdoc />
    public async Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(Guid currentUserId, CancelSubscriptionInput input)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        return await userBillingGAgent.CancelSubscriptionAsync(new CancelSubscriptionDto
        {
            UserId = currentUserId,
            SubscriptionId = input.SubscriptionId,
            CancellationReason = string.Empty,
            CancelAtPeriodEnd = true
        });
    }

    /// <inheritdoc />
    public async Task<AppStoreSubscriptionResponseDto> VerifyAppStoreReceiptAsync(Guid currentUserId, VerifyAppStoreReceiptInput input)
    {
        var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
        return await userBillingGAgent.CreateAppStoreSubscriptionAsync(new CreateAppStoreSubscriptionDto
        {
            UserId = currentUserId.ToString(),
            SandboxMode = input.SandboxMode,
            TransactionId = input.TransactionId
        });
    }

    /// <inheritdoc />
    public async Task<PaymentVerificationResponseDto> VerifyGooglePlayTransactionAsync(Guid currentUserId, GooglePlayTransactionVerificationRequestDto input)
    {
        _logger.LogInformation("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Starting verification for userId: {UserId}, transactionId: {TransactionId}",
            currentUserId, input.TransactionIdentifier);

        // Validate input parameters
        if (string.IsNullOrWhiteSpace(input.TransactionIdentifier))
        {
            _logger.LogWarning("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Invalid transaction identifier for userId: {UserId}", currentUserId);
            return new PaymentVerificationResponseDto
            {
                IsValid = false,
                Message = "Invalid transaction identifier",
                ErrorCode = "INVALID_TRANSACTION_ID"
            };
        }

        try
        {
            _logger.LogDebug("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Getting UserBillingGAgent for userId: {UserId}", currentUserId);
            var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);

            // Convert to GAgents DTO
            var gagentsDto = new GooglePlayTransactionVerificationDto
            {
                UserId = currentUserId.ToString(),
                TransactionIdentifier = input.TransactionIdentifier
            };

            _logger.LogDebug("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Calling UserBillingGAgent.VerifyGooglePlayTransactionAsync for userId: {UserId}", currentUserId);
            var result = await userBillingGAgent.VerifyGooglePlayTransactionAsync(gagentsDto);

            _logger.LogInformation("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Verification completed for userId: {UserId}, success: {IsValid}, message: {Message}, errorCode: {ErrorCode}, productId: {ProductId}",
                currentUserId, result.IsValid, result.Message, result.ErrorCode, result.ProductId);

            // Convert back to response DTO
            var response = new PaymentVerificationResponseDto
            {
                IsValid = result.IsValid,
                Message = result.Message ?? string.Empty,
                OrderId = string.Empty, // OrderId not available in GAgents result, will be set from Transaction
                ProductId = result.ProductId ?? string.Empty,
                SubscriptionEndDate = result.SubscriptionEndDate,
                PurchaseTimeMillis = result.PurchaseTimeMillis ?? 0,
                ErrorCode = result.ErrorCode ?? string.Empty
            };

            _logger.LogDebug("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Response created for userId: {UserId}, responseValid: {ResponseIsValid}, responseErrorCode: {ResponseErrorCode}", 
                currentUserId, response.IsValid, response.ErrorCode);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Exception occurred during Google Play transaction verification. UserId: {UserId}, TransactionId: {TransactionId}, ExceptionType: {ExceptionType}, ExceptionMessage: {ExceptionMessage}, StackTrace: {StackTrace}", 
                currentUserId, input.TransactionIdentifier, ex.GetType().Name, ex.Message, ex.StackTrace);
            
            // Log additional details for common exception types
            if (ex is TimeoutException)
            {
                _logger.LogError("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Timeout exception detected for userId: {UserId} - possible Orleans cluster or network issues", currentUserId);
            }
            else if (ex is System.Net.Http.HttpRequestException)
            {
                _logger.LogError("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] HTTP request exception detected for userId: {UserId} - possible Google Play API connectivity issues", currentUserId);
            }
            else if (ex is Orleans.Runtime.OrleansException)
            {
                _logger.LogError("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] Orleans exception detected for userId: {UserId} - possible Orleans cluster issues", currentUserId);
            }
            else if (ex is System.Text.Json.JsonException || ex is Newtonsoft.Json.JsonException)
            {
                _logger.LogError("[GodGPTPaymentService][VerifyGooglePlayTransactionAsync] JSON serialization exception detected for userId: {UserId} - possible data format issues", currentUserId);
            }
            
            return new PaymentVerificationResponseDto
            {
                IsValid = false,
                Message = "Transaction verification failed",
                ErrorCode = "VERIFICATION_ERROR"
            };
        }
    }
}
