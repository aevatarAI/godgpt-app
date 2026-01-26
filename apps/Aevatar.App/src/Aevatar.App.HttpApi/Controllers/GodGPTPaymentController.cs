using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.GodGPT.Dtos;
using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Providers;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IStateIndexService? _stateIndexService;
    private readonly Dictionary<string, int> _productPlanTypes; // productId/priceId -> PlanType
    private readonly Dictionary<string, bool> _productIsUltimate; // productId/priceId -> IsUltimate

    public GodGPTPaymentController(
        ILogger<GodGPTPaymentController> logger,
        IPaymentService paymentService,
        IOptions<StripeOptions>? stripeOptions = null,
        IOptions<ApplePayOptions>? appleOptions = null,
        IOptions<GooglePlayOptions>? googleOptions = null,
        IStateIndexService? stateIndexService = null)
    {
        _logger = logger;
        _paymentService = paymentService;
        _stateIndexService = stateIndexService;
        
        // Build unified product -> PlanType lookup from all platforms
        _productPlanTypes = BuildProductPlanTypeLookup(
            stripeOptions?.Value.Products,
            appleOptions?.Value.Products,
            googleOptions?.Value.Products);
        
        // Build unified product -> IsUltimate lookup from all platforms
        _productIsUltimate = BuildProductIsUltimateLookup(
            stripeOptions?.Value.Products,
            appleOptions?.Value.Products,
            googleOptions?.Value.Products);
    }
    
    private static Dictionary<string, int> BuildProductPlanTypeLookup(
        List<StripeProductConfig>? stripeProducts,
        List<AppleProductConfig>? appleProducts,
        List<GoogleProductConfig>? googleProducts)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        // Stripe uses PriceId as ProductId
        if (stripeProducts != null)
        {
            foreach (var p in stripeProducts)
            {
                if (!string.IsNullOrEmpty(p.PriceId))
                    lookup[p.PriceId] = p.PlanType;
            }
        }
        
        // Apple uses ProductId
        if (appleProducts != null)
        {
            foreach (var p in appleProducts)
            {
                if (!string.IsNullOrEmpty(p.ProductId))
                    lookup[p.ProductId] = p.PlanType;
            }
        }
        
        // Google uses ProductId
        if (googleProducts != null)
        {
            foreach (var p in googleProducts)
            {
                if (!string.IsNullOrEmpty(p.ProductId))
                    lookup[p.ProductId] = p.PlanType;
            }
        }
        
        return lookup;
    }
    
    private static Dictionary<string, bool> BuildProductIsUltimateLookup(
        List<StripeProductConfig>? stripeProducts,
        List<AppleProductConfig>? appleProducts,
        List<GoogleProductConfig>? googleProducts)
    {
        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        
        // Stripe uses PriceId as ProductId
        if (stripeProducts != null)
        {
            foreach (var p in stripeProducts)
            {
                if (!string.IsNullOrEmpty(p.PriceId))
                    lookup[p.PriceId] = p.IsUltimate;
            }
        }
        
        // Apple uses ProductId
        if (appleProducts != null)
        {
            foreach (var p in appleProducts)
            {
                if (!string.IsNullOrEmpty(p.ProductId))
                    lookup[p.ProductId] = p.IsUltimate;
            }
        }
        
        // Google uses ProductId
        if (googleProducts != null)
        {
            foreach (var p in googleProducts)
            {
                if (!string.IsNullOrEmpty(p.ProductId))
                    lookup[p.ProductId] = p.IsUltimate;
            }
        }
        
        return lookup;
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
        var pageIndex = input?.PageIndex ?? 1;
        var pageSize = input?.PageSize ?? 10;
        
        // Try CQRS query first for full data
        if (_stateIndexService != null)
        {
            try
            {
                var query = new StateQuery
                {
                    AgentType = "Aevatar.Payment.Agents.PaymentRecordGAgent",
                    QueryString = $"userId.keyword:\"{currentUserId}\"",
                    PageIndex = 0,
                    PageSize = pageSize * 3, // Fetch extra for filtering
                    SortFields = new List<string> { "createdAt:desc" }
                };

                var queryResult = await _stateIndexService.QueryAsync(query);
                
                // Filter stale Processing records (like old code)
                var oneDayAgo = DateTime.UtcNow.AddDays(-1);
                
                var result = queryResult.Items
                    .Select(item => MapToPaymentSummaryDto(item, _productPlanTypes, _productIsUltimate))
                    .Where(dto => 
                    {
                        // Keep all non-Processing records
                        if (dto.Status != (int)PaymentStatus.Processing)
                            return true;
                        // For Processing, keep if recent (< 1 day)
                        return dto.CreatedAtRaw > oneDayAgo;
                    })
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                
                _logger.LogDebug(
                    "[GodGPTPaymentController][GetPaymentHistoryAsync] CQRS query returned {Count} records for user {UserId}, duration: {Duration}ms",
                    result.Count, currentUserId, stopwatch.ElapsedMilliseconds);
                    
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[GodGPTPaymentController][GetPaymentHistoryAsync] CQRS query failed, falling back");
            }
        }
        
        // Fallback to basic PaymentService
        var history = await _paymentService.GetPaymentHistoryAsync(currentUserId, pageIndex, pageSize);
        
        var fallbackResult = history.Select(h => new PaymentSummaryDto
        {
            PaymentGrainId = Guid.TryParse(h.PaymentId, out var id) ? id : Guid.Empty,
            Amount = h.Amount,
            Currency = h.Currency,
            Status = (int)h.Status,
            Platform = (int)h.Platform,
            CreatedAtRaw = h.CreatedAt,
            CompletedAtRaw = h.CompletedAt
        }).ToList();
        
        _logger.LogDebug("[GodGPTPaymentController][GetPaymentHistoryAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return fallbackResult;
    }
    
    private static PaymentSummaryDto MapToPaymentSummaryDto(
        StateQueryResult item, 
        Dictionary<string, int> productPlanTypes,
        Dictionary<string, bool> productIsUltimate)
    {
        var dto = new PaymentSummaryDto();
        var data = item.Data;
        
        // Core identifiers - AgentId is the PaymentGrainId
        dto.PaymentGrainId = Guid.TryParse(item.AgentId, out var pid) ? pid : Guid.Empty;
        
        if (data.TryGetValue("externalOrderId", out var orderId))
            dto.OrderId = orderId?.ToString();
        if (data.TryGetValue("userId", out var userId))
            dto.UserId = Guid.TryParse(userId?.ToString(), out var uid) ? uid : Guid.Empty;
        
        // Get priceId/productId for config lookup
        string? priceIdStr = null;
        string? productIdStr = null;
        if (data.TryGetValue("priceId", out var priceId) && !string.IsNullOrEmpty(priceId?.ToString()))
            priceIdStr = priceId.ToString();
        if (data.TryGetValue("productId", out var productId) && !string.IsNullOrEmpty(productId?.ToString()))
            productIdStr = productId.ToString();
        
        // Plan info - uses legacy PlanType values (Day=1, Month=2, Year=3, Week=4)
        // Priority: ES billingCycle (now stores legacy values) -> product config fallback
        int planType = 0;
        
        // Try ES billingCycle first (stores legacy PlanType values after fix)
        if (data.TryGetValue("billingCycle", out var billingCycle))
        {
            planType = Convert.ToInt32(billingCycle ?? 0);
        }
        
        // Fallback to product config if billingCycle is 0 (old records or migration data)
        if (planType == 0)
        {
            // ES stores Stripe's priceId in productId field, Apple/Google use productId directly
            // So try productId first (works for all platforms)
            if (!string.IsNullOrEmpty(productIdStr) && productPlanTypes.TryGetValue(productIdStr, out var pt1))
                planType = pt1;
            else if (!string.IsNullOrEmpty(priceIdStr) && productPlanTypes.TryGetValue(priceIdStr, out var pt2))
                planType = pt2;
        }
        
        dto.PlanType = planType;
        
        // Determine isUltimate from metadata or product config
        bool isUltimate = false;
        if (data.TryGetValue("businessMetadata", out var metadata) && metadata != null)
        {
            var metaDict = ParseMetadata(metadata);
            if (metaDict.TryGetValue("is_ultimate", out var isUltimateStr))
                isUltimate = bool.TryParse(isUltimateStr, out var u) && u;
            else if (metaDict.TryGetValue("isUltimate", out var isUltimateStr2))
                isUltimate = bool.TryParse(isUltimateStr2, out var u2) && u2;
        }
        
        // Fallback to product config lookup
        if (!isUltimate)
        {
            if (!string.IsNullOrEmpty(productIdStr) && productIsUltimate.TryGetValue(productIdStr, out var u1))
                isUltimate = u1;
            else if (!string.IsNullOrEmpty(priceIdStr) && productIsUltimate.TryGetValue(priceIdStr, out var u2))
                isUltimate = u2;
        }
        
        // Get membership level: "Premium" or "Ultimate" (matches old MembershipLevel constants)
        dto.MembershipLevel = GetMembershipLevelFromIsUltimate(isUltimate);
        
        // Note: planType indicates billing cycle:
        // 1 = Day, 2 = Month, 3 = Year, 4 = Week
        // Weekly subscription is identified by planType == 4
        
        // Amount info
        if (data.TryGetValue("amount", out var amount))
            dto.Amount = Convert.ToInt64(amount ?? 0) / 100m;
        if (data.TryGetValue("currency", out var currency))
            dto.Currency = currency?.ToString() ?? "USD";
        if (data.TryGetValue("netAmount", out var netAmount) && netAmount != null)
            dto.AmountNetTotal = Convert.ToInt64(netAmount) / 100m;
        
        // Status and platform
        if (data.TryGetValue("status", out var status))
            dto.Status = Convert.ToInt32(status ?? 0);
        if (data.TryGetValue("platform", out var platform))
            dto.Platform = Convert.ToInt32(platform ?? 0);
        
        // Timestamps
        if (data.TryGetValue("createdAt", out var createdAt) && createdAt != null)
            dto.CreatedAtRaw = ParseDateTime(createdAt);
        if (data.TryGetValue("completedAt", out var completedAt) && completedAt != null)
            dto.CompletedAtRaw = ParseDateTime(completedAt);
        
        // Subscription period dates - calculate from completedAt + planType (like old code)
        // Old code: CalculateSubscriptionDurationAsync calculates based on PlanType
        if (dto.CompletedAtRaw.HasValue && dto.CompletedAtRaw.Value != DateTime.MinValue)
        {
            dto.SubscriptionStartDateRaw = dto.CompletedAtRaw.Value;
            dto.SubscriptionEndDateRaw = CalculateSubscriptionEndDate(planType, dto.CompletedAtRaw.Value);
        }
        
        // Subscription details
        if (data.TryGetValue("subscriptionId", out var subId))
            dto.SubscriptionId = subId?.ToString();
        dto.PriceId = priceIdStr;
        
        // Environment
        if (data.TryGetValue("environment", out var env))
            dto.AppStoreEnvironment = env?.ToString();
        
        // Trial info from metadata
        if (metadata != null)
        {
            var metaDict = ParseMetadata(metadata);
            if (metaDict.TryGetValue("is_trial", out var isTrial))
                dto.IsTrial = bool.TryParse(isTrial, out var t) && t;
            if (metaDict.TryGetValue("trial_code", out var trialCode))
                dto.TrialCode = trialCode;
        }
        
        // Payment type
        if (data.TryGetValue("paymentMode", out var paymentMode))
            dto.PaymentType = Convert.ToInt32(paymentMode ?? 0);
        
        return dto;
    }
    
    private static DateTime ParseDateTime(object? value)
    {
        if (value == null) return DateTime.MinValue;
        if (value is DateTime dt) return dt;
        if (DateTime.TryParse(value.ToString(), out var parsed)) return parsed;
        return DateTime.MinValue;
    }
    
    private static Dictionary<string, string> ParseMetadata(object? value)
    {
        if (value == null) return new Dictionary<string, string>();
        try
        {
            if (value is string jsonStr && !string.IsNullOrEmpty(jsonStr) && jsonStr != "{}")
                return JsonSerializer.Deserialize<Dictionary<string, string>>(jsonStr) ?? new();
            if (value is JsonElement elem)
                return JsonSerializer.Deserialize<Dictionary<string, string>>(elem.GetRawText()) ?? new();
        }
        catch { }
        return new Dictionary<string, string>();
    }
    
    private static string? GetMembershipLevel(int billingCycle)
    {
        return billingCycle switch
        {
            1 => "Weekly",
            2 => "Monthly",
            3 => "Quarterly", 
            4 => "Yearly",
            5 => "Premium",
            _ => null
        };
    }
    
    /// <summary>
    /// Get membership level from isUltimate flag (matches old MembershipLevel constants)
    /// Old API returns "Premium" or "Ultimate", not cycle names
    /// </summary>
    private static string GetMembershipLevelFromIsUltimate(bool isUltimate)
    {
        return isUltimate ? "Ultimate" : "Premium";
    }
    
    /// <summary>
    /// Calculate subscription end date based on PlanType (like old code: GetSubscriptionEndDate)
    /// Legacy PlanType: Day=1, Month=2, Year=3, Week=4
    /// </summary>
    private static DateTime CalculateSubscriptionEndDate(int planType, DateTime startDate)
    {
        return planType switch
        {
            1 => startDate.AddDays(1),    // Day
            2 => startDate.AddDays(30),   // Month
            3 => startDate.AddDays(365),  // Year
            4 => startDate.AddDays(7),    // Week
            _ => startDate.AddDays(30)    // Default to Month
        };
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

/// <summary>
/// Payment summary DTO - matches old API response format for backward compatibility
/// </summary>
public class PaymentSummaryDto
{
    // Core identifiers
    public Guid PaymentGrainId { get; set; }
    public string? OrderId { get; set; }
    public Guid UserId { get; set; }
    
    // Plan info
    public int PlanType { get; set; }
    
    // Amount info
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? AmountNetTotal { get; set; }
    
    // Status and platform
    public int Status { get; set; }
    public int Platform { get; set; }
    
    // Timestamps - internal use for filtering
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime CreatedAtRaw { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime? CompletedAtRaw { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime? SubscriptionStartDateRaw { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime? SubscriptionEndDateRaw { get; set; }
    
    // Timestamps - API output (ISO8601 string)
    public string CreatedAt => DateTimeFormatHelper.ToIso8601String(CreatedAtRaw);
    public string? CompletedAt => CompletedAtRaw.HasValue 
        ? DateTimeFormatHelper.ToIso8601String(CompletedAtRaw.Value) 
        : null;
    public string? SubscriptionStartDate => SubscriptionStartDateRaw.HasValue
        ? DateTimeFormatHelper.ToIso8601String(SubscriptionStartDateRaw.Value)
        : null;
    public string? SubscriptionEndDate => SubscriptionEndDateRaw.HasValue
        ? DateTimeFormatHelper.ToIso8601String(SubscriptionEndDateRaw.Value)
        : null;
    
    // Subscription details
    public string? SubscriptionId { get; set; }
    
    // Product info
    public string? PriceId { get; set; }
    
    // Environment
    public string? AppStoreEnvironment { get; set; }
    
    // Membership
    public string? MembershipLevel { get; set; }
    
    // Trial info
    public bool IsTrial { get; set; }
    public string? TrialCode { get; set; }
    
    // Payment type (0 = subscription, 1 = one-time)
    public int PaymentType { get; set; }
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
