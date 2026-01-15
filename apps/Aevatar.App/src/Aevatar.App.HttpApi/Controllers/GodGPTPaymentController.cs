using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.GodGPT.Dtos;
using Aevatar.Payment.Abstractions;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using GrainPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using BillingCycle = Aevatar.Payment.Abstractions.BillingCycle;
using QuotaPlanType = Aevatar.Agents.GodGPT.Protos.UserQuota.QuotaPlanType;

namespace Aevatar.Controllers;

/// <summary>
/// Payment Controller - Compatibility layer for existing clients.
/// Internally delegates to the new PaymentService.
/// </summary>
[RemoteService]
[ControllerName("Payment")]
[Route("api/godgpt/payment")]
[Authorize]
public class GodGPTPaymentController : AevatarController
{
    private readonly ILogger<GodGPTPaymentController> _logger;
    private readonly IPaymentService _paymentService;

    public GodGPTPaymentController(
        ILogger<GodGPTPaymentController> logger,
        IPaymentService paymentService)
    {
        _logger = logger;
        _paymentService = paymentService;
    }

    [HttpGet("keys")]
    public Task<StripePaymentKeysDto> GetStripePaymentKeysAsync()
    {
        // PublishableKey should come from configuration, kept empty for security
        return Task.FromResult(new StripePaymentKeysDto
        {
            PublishableKey = string.Empty
        });
    }

    [HttpGet("products")]
    public async Task<List<StripeProductDto>> GetStripeProductsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var products = await _paymentService.GetProductsAsync(PaymentPlatform.Stripe);
        
        var result = products.Select(p => new StripeProductDto
        {
            PriceId = p.ProductId,
            // Use original PlanType from config metadata (matches legacy API)
            PlanType = GetOriginalPlanType(p),
            Mode = "subscription",
            Amount = p.Price,
            Currency = p.Currency,
            // Use dailyAvgPrice from metadata (pure number format like "0.85")
            DailyAvgPrice = GetDailyAvgPrice(p),
            IsUltimate = p.PlanType == PlanType.Premium,
            Credits = 0
        }).ToList();
        
        _logger.LogDebug("[GodGPTPaymentController][GetStripeProductsAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return result;
    }

    [HttpGet("iap-products")]
    public async Task<List<AppleProductDto>> GetAppleProductsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var products = await _paymentService.GetProductsAsync(PaymentPlatform.AppStore);
        
        var result = products.Select(p => new AppleProductDto
        {
            ProductId = p.ProductId,
            Name = p.Name,
            Description = p.Description,
            // Use original PlanType from config metadata (matches legacy API)
            PlanType = (int)GetOriginalPlanType(p),
            Amount = p.Price,
            Currency = p.Currency,
            // Use dailyAvgPrice from metadata (pure number format)
            DailyAvgPrice = GetDailyAvgPrice(p)
        }).ToList();
        
        _logger.LogDebug("[GodGPTPaymentController][GetAppleProductsAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return result;
    }

    [HttpPost("create-checkout-session")]
    public async Task<IActionResult> CreateCheckoutSessionAsync(CreateCheckoutSessionInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        if (string.IsNullOrWhiteSpace(input.PriceId))
            return BadRequest("PriceId cannot be empty");
        
        try
        {
            var result = await _paymentService.CreateSubscriptionAsync(
                currentUserId,
                PaymentPlatform.Stripe,
                new SubscriptionRequest
                {
                    ProductId = input.PriceId,
                    CancelUrl = input.CancelUrl,
                    Mode = input.Mode,
                    UiMode = input.UiMode
                });

            _logger.LogDebug("[GodGPTPaymentController][CreateCheckoutSessionAsync] userId: {UserId}, uiMode: {UiMode}, duration: {Duration}ms",
                currentUserId, input.UiMode, stopwatch.ElapsedMilliseconds);

            // Return format compatible with legacy API
            // EMBEDDED mode: return clientSecret (string)
            // HOSTED mode: return session URL (string)
            if (string.Equals(input.UiMode, "embedded", StringComparison.OrdinalIgnoreCase))
            {
                var clientSecret = result.AdditionalData.GetValueOrDefault("clientSecret")?.ToString() ?? string.Empty;
                return Ok(clientSecret);
            }
            else
            {
                return Ok(result.SessionUrl ?? string.Empty);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[GodGPTPaymentController][CreateCheckoutSessionAsync] error");
            return BadRequest();
        }
    }

    [HttpPost("create-subscription")]
    public async Task<SubscriptionResponseDto> CreateSubscriptionAsync(CreateSubscriptionInput input)
    {
        _logger.LogWarning("CreateSubscriptionAsync Platform={Platform}", input.DevicePlatform);
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        var result = await _paymentService.CreateSubscriptionAsync(
            currentUserId,
            PaymentPlatform.Stripe,
            new SubscriptionRequest
            {
                ProductId = input.PriceId,
                Metadata = input.Metadata ?? new Dictionary<string, string>()
            });

        _logger.LogDebug("[GodGPTPaymentController][CreateSubscriptionAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return new SubscriptionResponseDto
        {
            SubscriptionId = result.SubscriptionId ?? string.Empty,
            CustomerId = result.CustomerId ?? string.Empty,
            ClientSecret = result.AdditionalData.GetValueOrDefault("clientSecret")?.ToString() ?? string.Empty
        };
    }

    [HttpGet("list")]
    public async Task<List<PaymentSummaryDto>> GetPaymentHistoryAsync([FromQuery] GetPaymentHistoryInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var history = await _paymentService.GetPaymentHistoryAsync(
            currentUserId, 
            input?.PageIndex ?? 1, 
            input?.PageSize ?? 10);
        
        var result = history.Select(h => new PaymentSummaryDto
        {
            PaymentId = h.PaymentId,
            ProductName = h.ProductName,
            Amount = h.Amount,
            Currency = h.Currency,
            Status = h.Status.ToString(),
            CreatedAt = DateTimeFormatHelper.ToIso8601String(h.CreatedAt)
        }).ToList();
        
        _logger.LogDebug("[GodGPTPaymentController][GetPaymentHistoryAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return result;
    }

    [HttpPost("customer")]
    public async Task<Aevatar.Application.Grains.ChatManager.Dtos.GetCustomerResponseDto> GetStripeCustomerAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var result = await _paymentService.GetStripeCustomerAsync(currentUserId);
        
        _logger.LogDebug("[GodGPTPaymentController][GetStripeCustomerAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return new Aevatar.Application.Grains.ChatManager.Dtos.GetCustomerResponseDto
        {
            EphemeralKey = result.EphemeralKey ?? string.Empty,
            Customer = result.CustomerId ?? string.Empty,
            PublishableKey = result.PublishableKey ?? string.Empty
        };
    }

    [HttpPost("cancel-subscription")]
    public async Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var result = await _paymentService.CancelSubscriptionAsync(
            currentUserId,
            PaymentPlatform.Stripe,
            new CancellationRequest
            {
                SubscriptionId = input.SubscriptionId,
                Reason = null,
                Immediate = false // Default: cancel at period end
            });
        
        _logger.LogDebug("[GodGPTPaymentController][CancelSubscriptionAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return new CancelSubscriptionResponseDto
        {
            Success = result.Success,
            Message = result.ErrorMessage ?? "Success",
            SubscriptionId = input.SubscriptionId,
            Status = result.Success ? "cancelled" : "failed",
            CancelledAt = result.EffectiveDate
        };
    }

    [HttpPost("refunded")]
    public Task<bool> RefundedAsync()
    {
        return Task.FromResult(true);
    }

    [HttpPost("verify-receipt")]
    public async Task<AppStoreSubscriptionResponseDto> VerifyAppStoreReceiptAsync(VerifyAppStoreReceiptInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var result = await _paymentService.CreateSubscriptionAsync(
            currentUserId,
            PaymentPlatform.AppStore,
            new SubscriptionRequest
            {
                ProductId = input.ProductId,
                TransactionId = input.TransactionId,
                ReceiptData = input.ReceiptData,
                IsSandbox = input.SandboxMode
            });

        _logger.LogDebug("[GodGPTPaymentController][VerifyAppStoreReceiptAsync] userId: {UserId}, sandboxMode: {SandboxMode}, duration: {Duration}ms",
            currentUserId, input.SandboxMode, stopwatch.ElapsedMilliseconds);

        return new AppStoreSubscriptionResponseDto
        {
            Success = result.Success,
            Error = result.ErrorMessage,
            SubscriptionId = result.SubscriptionId,
            ExpiresAt = DateTimeFormatHelper.ToIso8601String(result.ExpiresAt)
        };
    }

    [HttpPost("google-play/verify-transaction")]
    public async Task<PaymentVerificationResponseDto> VerifyGooglePlayTransactionAsync(
        GooglePlayTransactionVerificationRequestDto input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        _logger.LogInformation("[GodGPTPaymentController][VerifyGooglePlayTransactionAsync] userId: {UserId}, transactionId: {TransactionId}",
            currentUserId, input.TransactionIdentifier);

        if (input == null || string.IsNullOrEmpty(input.TransactionIdentifier))
        {
            return new PaymentVerificationResponseDto
            {
                IsValid = false,
                Message = "Invalid request",
                ErrorCode = "INVALID_REQUEST"
            };
        }

        try
        {
            var result = await _paymentService.CreateSubscriptionAsync(
                currentUserId,
                PaymentPlatform.GooglePlay,
                new SubscriptionRequest
                {
                    TransactionId = input.TransactionIdentifier
                });

            _logger.LogInformation("[GodGPTPaymentController][VerifyGooglePlayTransactionAsync] userId: {UserId}, transactionId: {TransactionId}, duration: {Duration}ms, result: {IsValid}",
                currentUserId, input.TransactionIdentifier, stopwatch.ElapsedMilliseconds, result.Success);

            return new PaymentVerificationResponseDto
            {
                IsValid = result.Success,
                Message = result.Success ? "Verification successful" : result.ErrorMessage ?? "Verification failed",
                ErrorCode = result.Success ? null : "VERIFICATION_FAILED",
                ProductId = result.AdditionalData.GetValueOrDefault("productId")?.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTPaymentController][VerifyGooglePlayTransactionAsync] error for userId: {UserId}", currentUserId);
            return new PaymentVerificationResponseDto
            {
                IsValid = false,
                Message = "Internal server error",
                ErrorCode = "CONTROLLER_ERROR"
            };
        }
    }

    [Obsolete("Use has-active-subscription instead")]
    [HttpGet("has-apple-subscription")]
    public async Task<bool> HasActiveAppleSubscriptionAsync()
    {
        var currentUserId = (Guid)CurrentUser.Id!;
        var status = await _paymentService.GetUserSubscriptionStatusAsync(currentUserId);
        return status.ActiveSubscriptions.Any(s => s.Platform == PaymentPlatform.AppStore);
    }

    [HttpGet("has-active-subscription")]
    public async Task<ActiveSubscriptionStatusDto> HasActiveSubscriptionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        
        var status = await _paymentService.GetUserSubscriptionStatusAsync(currentUserId);
        
        _logger.LogDebug("[GodGPTPaymentController][HasActiveSubscriptionAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return new ActiveSubscriptionStatusDto
        {
            HasActiveSubscription = status.HasActiveSubscription,
            HasActiveStripeSubscription = status.ActiveSubscriptions.Any(s => s.Platform == PaymentPlatform.Stripe),
            HasActiveAppleSubscription = status.ActiveSubscriptions.Any(s => s.Platform == PaymentPlatform.AppStore),
            HasActiveGooglePlaySubscription = status.ActiveSubscriptions.Any(s => s.Platform == PaymentPlatform.GooglePlay)
        };
    }

    #region Helper Methods

    /// <summary>
    /// Gets original PlanType from product metadata (matches legacy API: 1=Day, 2=Month, 3=Year, 4=Week)
    /// </summary>
    private static QuotaPlanType GetOriginalPlanType(ProductDto product)
    {
        if (product.Metadata.TryGetValue("originalPlanType", out var planTypeStr) 
            && int.TryParse(planTypeStr, out var planType))
        {
            return (QuotaPlanType)planType;
        }
        // Fallback: map from BillingCycle
        return MapBillingCycleToPlanType(product.BillingCycle).ToQuotaPlanType();
    }

    /// <summary>
    /// Gets dailyAvgPrice from product metadata (pure number format like "0.85")
    /// </summary>
    private static string GetDailyAvgPrice(ProductDto product)
    {
        if (product.Metadata.TryGetValue("dailyAvgPrice", out var dailyAvgPrice))
        {
            return dailyAvgPrice;
        }
        // Fallback: calculate from price and BillingCycle
        return CalculateDailyAvgPriceLegacy(product.Price, product.BillingCycle);
    }

    /// <summary>
    /// Maps BillingCycle (new design) to old PlanType (Day/Month/Year/Week)
    /// </summary>
    private static GrainPlanType MapBillingCycleToPlanType(BillingCycle billingCycle)
    {
        return billingCycle switch
        {
            BillingCycle.Daily => GrainPlanType.Day,
            BillingCycle.Weekly => GrainPlanType.Week,
            BillingCycle.Monthly => GrainPlanType.Month,
            BillingCycle.Quarterly => GrainPlanType.Month,
            BillingCycle.Yearly => GrainPlanType.Year,
            BillingCycle.Lifetime => GrainPlanType.Year,
            _ => GrainPlanType.None
        };
    }

    /// <summary>
    /// Legacy dailyAvgPrice calculation (pure number format)
    /// </summary>
    private static string CalculateDailyAvgPriceLegacy(decimal price, BillingCycle billingCycle)
    {
        var days = billingCycle switch
        {
            BillingCycle.Daily => 1,
            BillingCycle.Weekly => 7,
            BillingCycle.Monthly => 30,
            BillingCycle.Quarterly => 90,
            BillingCycle.Yearly => 365,
            BillingCycle.Lifetime => 3650,
            _ => 30
        };
        return Math.Round(price / days, 2).ToString("F2");
    }

    #endregion
}

#region Compatibility DTOs

public class StripePaymentKeysDto
{
    public string PublishableKey { get; set; } = string.Empty;
}

public class PaymentSummaryDto
{
    public string PaymentId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class AppStoreSubscriptionResponseDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SubscriptionId { get; set; }
    public string? ExpiresAt { get; set; }
}

public class GetPaymentHistoryInput
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

#endregion
