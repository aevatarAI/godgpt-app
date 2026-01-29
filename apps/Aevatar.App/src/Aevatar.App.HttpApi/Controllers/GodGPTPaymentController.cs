using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.GodGPT.Dtos;
using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Providers;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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
    private readonly IGAgentActorFactory? _actorFactory;
    private readonly Dictionary<string, int> _productPlanTypes; // productId/priceId -> PlanType
    private readonly Dictionary<string, bool> _productIsUltimate; // productId/priceId -> IsUltimate

    public GodGPTPaymentController(
        ILogger<GodGPTPaymentController> logger,
        IPaymentService paymentService,
        IOptions<StripeOptions>? stripeOptions = null,
        IOptions<ApplePayOptions>? appleOptions = null,
        IOptions<GooglePlayOptions>? googleOptions = null,
        IStateIndexService? stateIndexService = null,
        IGAgentActorFactory? actorFactory = null)
    {
        _logger = logger;
        _paymentService = paymentService;
        _stateIndexService = stateIndexService;
        _actorFactory = actorFactory;
        
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
        
        // Validate subscription upgrade path before creating checkout session
        var (isValid, errorMessage) = await ValidateSubscriptionUpgradePathAsync(currentUserId, input.PriceId);
        if (!isValid)
        {
            _logger.LogWarning(
                "[GodGPTPaymentController][CreateCheckoutSessionAsync] Upgrade validation failed for user {UserId}: {Error}",
                currentUserId, errorMessage);
            return BadRequest(errorMessage);
        }
        
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

        // Validate subscription upgrade path before creating subscription
        var (isValid, errorMessage) = await ValidateSubscriptionUpgradePathAsync(currentUserId, input.PriceId);
        if (!isValid)
        {
            _logger.LogWarning(
                "[GodGPTPaymentController][CreateSubscriptionAsync] Upgrade validation failed for user {UserId}: {Error}",
                currentUserId, errorMessage);
            throw new UserFriendlyException(errorMessage ?? "Invalid subscription upgrade path");
        }

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

    [HttpGet("list-test")]
    [AllowAnonymous]
    public async Task<List<PaymentSummaryDto>> GetPaymentHistoryTestAsync([FromQuery] GetPaymentHistoryInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        // Test userId from inserted data
        var currentUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var pageIndex = input?.PageIndex ?? 1;
        var pageSize = input?.PageSize ?? 10;
        
        return await GetPaymentHistoryInternalAsync(currentUserId, pageIndex, pageSize, stopwatch);
    }

    [HttpGet("list")]
    public async Task<List<PaymentSummaryDto>> GetPaymentHistoryAsync([FromQuery] GetPaymentHistoryInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var pageIndex = input?.PageIndex ?? 1;
        var pageSize = input?.PageSize ?? 10;
        
        return await GetPaymentHistoryInternalAsync(currentUserId, pageIndex, pageSize, stopwatch);
    }
    
    private async Task<List<PaymentSummaryDto>> GetPaymentHistoryInternalAsync(
        Guid currentUserId, 
        int pageIndex, 
        int pageSize, 
        Stopwatch stopwatch)
    {
        
        // Try CQRS query first for full data
        if (_stateIndexService != null)
        {
            try
            {
                // Fetch more records to account for transaction expansion
                // Each payment record might expand to multiple transaction records
                var query = new StateQuery
                {
                    AgentType = "Aevatar.Payment.Agents.PaymentRecordGAgent",
                    QueryString = $"userId.keyword:\"{currentUserId}\"",
                    PageIndex = 0,
                    PageSize = pageSize * 10, // Fetch more to account for transaction expansion
                    SortFields = new List<string> { "createdAt:desc" }
                };

                var queryResult = await _stateIndexService.QueryAsync(query);
                
                // Filter stale Processing records (like old code)
                var oneDayAgo = DateTime.UtcNow.AddDays(-1);
                
                // Expand transactions like old code's InvoiceDetails expansion
                var expandedItems = new List<PaymentSummaryDto>();
                foreach (var item in queryResult.Items)
                {
                    var transactions = ParseTransactionsFromData(item.Data, _logger);
                    
                    // Like old code: if no transactions or only 1, return record-level data
                    if (transactions == null || transactions.Count <= 1)
                    {
                        expandedItems.Add(MapToPaymentSummaryDto(item, _productPlanTypes, _productIsUltimate));
                    }
                    else
                    {
                        // Expand each transaction as a separate history item (like old code's InvoiceDetails)
                        foreach (var tx in transactions)
                        {
                            expandedItems.Add(MapTransactionToPaymentSummaryDto(item, tx, _productPlanTypes, _productIsUltimate));
                        }
                    }
                }
                
                var result = expandedItems
                    .Where(dto => 
                    {
                        // Keep all non-Processing records
                        if (dto.Status != (int)PaymentStatus.Processing)
                            return true;
                        // For Processing, keep if recent (< 1 day)
                        return dto.CreatedAtRaw > oneDayAgo;
                    })
                    .OrderByDescending(dto => dto.CreatedAtRaw)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                
                _logger.LogDebug(
                    "[GodGPTPaymentController][GetPaymentHistoryAsync] CQRS query: fetched {Fetched} records, expanded to {Expanded} items, filtered to {Result} records for user {UserId}, duration: {Duration}ms",
                    queryResult.Items.Count, expandedItems.Count, result.Count, currentUserId, stopwatch.ElapsedMilliseconds);
                    
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
    
    
    /// <summary>
    /// Parse transactions array from ES data using Protobuf JSON parser.
    /// StateDocumentConverter serializes repeated TransactionProto fields as JSON strings.
    /// Directly parse to TransactionProto objects - much simpler and type-safe!
    /// </summary>
    private static List<TransactionProto>? ParseTransactionsFromData(
        Dictionary<string, object?> data, 
        ILogger<GodGPTPaymentController>? logger = null)
    {
        try
        {
            if (!data.TryGetValue("transactions", out var transactionsObj) || transactionsObj == null)
                return null;
            
            string? transactionsJson = null;
            
            // Case 1: Already a List (after auto-deserialization by ConvertJsonElement)
            if (transactionsObj is List<object> list)
            {
                var result = new List<TransactionProto>();
                foreach (var item in list)
                {
                    try
                    {
                        // Convert to JSON string first, then parse with Protobuf
                        var jsonStr = item is JsonElement je ? je.GetRawText() : JsonSerializer.Serialize(item);
                        var txProto = JsonParser.Default.Parse<TransactionProto>(jsonStr);
                        result.Add(txProto);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "[GodGPTPaymentController][ParseTransactionsFromData] Failed to parse transaction from list item");
                    }
                }
                if (result.Count > 0)
                {
                    logger?.LogDebug(
                        "[GodGPTPaymentController][ParseTransactionsFromData] Found {Count} transactions (already deserialized)",
                        result.Count);
                    return result;
                }
            }
            
            // Case 2: JSON string - parse using Protobuf JSON parser
            if (transactionsObj is string str)
            {
                transactionsJson = str;
            }
            else if (transactionsObj is JsonElement jsonElem && jsonElem.ValueKind == JsonValueKind.String)
            {
                transactionsJson = jsonElem.GetString();
            }
            
            if (string.IsNullOrEmpty(transactionsJson) || transactionsJson == "[]")
                return null;
            
            // Handle escaped JSON string (double-quoted JSON string)
            if (transactionsJson.StartsWith("\"") && transactionsJson.EndsWith("\"") && transactionsJson.Length > 2)
            {
                var unescapedJson = JsonSerializer.Deserialize<string>(transactionsJson);
                if (!string.IsNullOrEmpty(unescapedJson))
                    transactionsJson = unescapedJson;
            }
            // Handle triple quotes (from ES raw JSON)
            else if (transactionsJson.StartsWith("\"\"\"") && transactionsJson.EndsWith("\"\"\"") && transactionsJson.Length > 6)
            {
                transactionsJson = transactionsJson.Substring(3, transactionsJson.Length - 6);
            }
            
            // Parse JSON array - data was serialized with System.Text.Json (not Protobuf JSON),
            // so Timestamp fields are in object format {"seconds":xxx,"nanos":xxx} instead of RFC3339 string.
            // We need to manually parse and map the fields.
            using var doc = JsonDocument.Parse(transactionsJson);
            var root = doc.RootElement;
            
            if (root.ValueKind != JsonValueKind.Array)
                return null;
            
            var resultList = new List<TransactionProto>();
            foreach (var txElement in root.EnumerateArray())
            {
                try
                {
                    var txProto = ParseTransactionFromJsonElement(txElement);
                    if (txProto != null)
                        resultList.Add(txProto);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex,
                        "[GodGPTPaymentController][ParseTransactionsFromData] Failed to parse transaction. JSON: {Json}",
                        txElement.GetRawText().Length > 200 ? txElement.GetRawText().Substring(0, 200) + "..." : txElement.GetRawText());
                }
            }
            
            if (resultList.Count > 0)
            {
                logger?.LogDebug(
                    "[GodGPTPaymentController][ParseTransactionsFromData] Successfully parsed {Count} transactions",
                    resultList.Count);
                return resultList;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "[GodGPTPaymentController][ParseTransactionsFromData] Failed to parse transactions. Type: {Type}",
                data.TryGetValue("transactions", out var tx) ? tx?.GetType().Name : "null");
            return null;
        }
    }
    
    /// <summary>
    /// Parse a single transaction from JsonElement (handles System.Text.Json serialized Timestamp format)
    /// </summary>
    private static TransactionProto? ParseTransactionFromJsonElement(JsonElement elem)
    {
        var tx = new TransactionProto();
        
        if (elem.TryGetProperty("transactionId", out var txId))
            tx.TransactionId = txId.GetString() ?? "";
        if (elem.TryGetProperty("externalTransactionId", out var extTxId))
            tx.ExternalTransactionId = extTxId.GetString() ?? "";
        if (elem.TryGetProperty("invoiceId", out var invId))
            tx.InvoiceId = invId.GetString() ?? "";
        if (elem.TryGetProperty("purchaseToken", out var pt))
            tx.PurchaseToken = pt.GetString() ?? "";
        if (elem.TryGetProperty("transactionType", out var txType))
            tx.TransactionType = txType.GetInt32();
        if (elem.TryGetProperty("status", out var status))
            tx.Status = status.GetInt32();
        if (elem.TryGetProperty("amount", out var amt))
            tx.Amount = amt.GetInt64();
        if (elem.TryGetProperty("currency", out var cur))
            tx.Currency = cur.GetString() ?? "USD";
        if (elem.TryGetProperty("netAmount", out var netAmt))
            tx.NetAmount = netAmt.GetInt64();
        if (elem.TryGetProperty("isTrial", out var trial))
            tx.IsTrial = trial.GetBoolean();
        if (elem.TryGetProperty("trialCode", out var trialCode))
            tx.TrialCode = trialCode.GetString() ?? "";
        
        // Product info per transaction (new fields)
        if (elem.TryGetProperty("productId", out var prodId))
            tx.ProductId = prodId.GetString() ?? "";
        if (elem.TryGetProperty("planType", out var planType))
            tx.PlanType = planType.GetInt32();
        if (elem.TryGetProperty("membershipLevel", out var memLevel))
            tx.MembershipLevel = memLevel.GetString() ?? "";
        
        // Parse Timestamp fields (System.Text.Json format: {"seconds":xxx,"nanos":xxx})
        if (elem.TryGetProperty("periodStart", out var ps))
            tx.PeriodStart = ParseTimestampFromJsonElement(ps);
        if (elem.TryGetProperty("periodEnd", out var pe))
            tx.PeriodEnd = ParseTimestampFromJsonElement(pe);
        if (elem.TryGetProperty("createdAt", out var ca))
            tx.CreatedAt = ParseTimestampFromJsonElement(ca);
        if (elem.TryGetProperty("completedAt", out var coa))
            tx.CompletedAt = ParseTimestampFromJsonElement(coa);
        
        return tx;
    }
    
    /// <summary>
    /// Parse Timestamp from JsonElement (handles {"seconds":xxx,"nanos":xxx} format)
    /// </summary>
    private static Timestamp? ParseTimestampFromJsonElement(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Null)
            return null;
        
        if (elem.ValueKind == JsonValueKind.Object)
        {
            long seconds = 0;
            int nanos = 0;
            if (elem.TryGetProperty("seconds", out var s))
                seconds = s.GetInt64();
            if (elem.TryGetProperty("nanos", out var n))
                nanos = n.GetInt32();
            return new Timestamp { Seconds = seconds, Nanos = nanos };
        }
        
        // Try parse as string (RFC3339 format)
        if (elem.ValueKind == JsonValueKind.String)
        {
            var str = elem.GetString();
            if (!string.IsNullOrEmpty(str) && DateTime.TryParse(str, out var dt))
                return Timestamp.FromDateTime(dt.ToUniversalTime());
        }
        
        return null;
    }
    
    /// <summary>
    /// Map a single transaction to PaymentSummaryDto (like old code's InvoiceDetails expansion)
    /// </summary>
    private static PaymentSummaryDto MapTransactionToPaymentSummaryDto(
        StateQueryResult item,
        TransactionProto tx,
        Dictionary<string, int> productPlanTypes,
        Dictionary<string, bool> productIsUltimate)
    {
        // Start with base record data
        var dto = MapToPaymentSummaryDto(item, productPlanTypes, productIsUltimate);
        
        // Override with transaction-level data - directly from Protobuf object!
        dto.Amount = tx.Amount / 100m;
        dto.Currency = tx.Currency;
        if (tx.NetAmount != 0)
            dto.AmountNetTotal = tx.NetAmount / 100m;
        dto.Status = tx.Status;
        
        // Transaction timestamps - directly from Protobuf Timestamp
        dto.CreatedAtRaw = tx.CreatedAt.ToDateTime();
        if (tx.CompletedAt != null && (tx.CompletedAt.Seconds != 0 || tx.CompletedAt.Nanos != 0))
            dto.CompletedAtRaw = tx.CompletedAt.ToDateTime();
        
        // Transaction period dates
        if (tx.PeriodStart != null && (tx.PeriodStart.Seconds != 0 || tx.PeriodStart.Nanos != 0))
            dto.SubscriptionStartDateRaw = tx.PeriodStart.ToDateTime();
        if (tx.PeriodEnd != null && (tx.PeriodEnd.Seconds != 0 || tx.PeriodEnd.Nanos != 0))
            dto.SubscriptionEndDateRaw = tx.PeriodEnd.ToDateTime();
        
        // Trial info from transaction
        dto.IsTrial = tx.IsTrial;
        if (!string.IsNullOrEmpty(tx.TrialCode))
            dto.TrialCode = tx.TrialCode;
        
        // Product info per transaction (new fields - matches old InvoiceDetail.PriceId/PlanType)
        if (!string.IsNullOrEmpty(tx.ProductId))
            dto.PriceId = tx.ProductId;
        if (tx.PlanType != 0)
        {
            dto.PlanType = tx.PlanType;
            // Recalculate subscription end date with transaction's plan type
            if (dto.CompletedAtRaw.HasValue && dto.CompletedAtRaw.Value != DateTime.MinValue)
                dto.SubscriptionEndDateRaw = CalculateSubscriptionEndDate(tx.PlanType, dto.CompletedAtRaw.Value);
        }
        if (!string.IsNullOrEmpty(tx.MembershipLevel))
            dto.MembershipLevel = tx.MembershipLevel;
        
        return dto;
    }
    
    /// <summary>
    /// Safely convert object to Int64 (handles JsonElement, string, and numeric types)
    /// </summary>
    private static long ConvertToInt64(object? value)
    {
        if (value == null) return 0;
        
        if (value is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Number)
                return elem.GetInt64();
            if (elem.ValueKind == JsonValueKind.String && long.TryParse(elem.GetString(), out var parsed))
                return parsed;
            return 0;
        }
        
        if (value is long l) return l;
        if (value is int i) return i;
        if (value is string str && long.TryParse(str, out var parsedStr))
            return parsedStr;
        
        try
        {
            return Convert.ToInt64(value);
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// Safely convert object to Int32 (handles JsonElement, string, and numeric types)
    /// </summary>
    private static int ConvertToInt32(object? value)
    {
        if (value == null) return 0;
        
        if (value is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Number)
                return elem.GetInt32();
            if (elem.ValueKind == JsonValueKind.String && int.TryParse(elem.GetString(), out var parsed))
                return parsed;
            return 0;
        }
        
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is string str && int.TryParse(str, out var parsedStr))
            return parsedStr;
        
        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// Parse timestamp from transaction data (supports both Protobuf format and ISO string)
    /// </summary>
    private static DateTime ParseTimestampFromTransaction(object? value)
    {
        if (value == null) return DateTime.MinValue;
        
        // Format 1: Protobuf Timestamp object { "seconds": 123, "nanos": 456 }
        if (value is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
        {
            if (elem.TryGetProperty("seconds", out var seconds))
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(seconds.GetInt64()).UtcDateTime;
                return dt;
            }
        }
        
        // Format 2: ISO date string or Dictionary with seconds
        if (value is Dictionary<string, object> dict && dict.TryGetValue("seconds", out var sec))
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(sec)).UtcDateTime;
            return dt;
        }
        
        // Format 3: Direct ISO string
        if (DateTime.TryParse(value.ToString(), out var parsed))
            return parsed;
        
        return DateTime.MinValue;
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
        // PriceId: prefer priceId field, fallback to productId (Stripe stores price ID in productId field)
        dto.PriceId = !string.IsNullOrEmpty(priceIdStr) ? priceIdStr : productIdStr;
        
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

    #region Subscription Upgrade Validation

    /// <summary>
    /// Validates if the upgrade path is allowed before creating a subscription.
    /// Prevents downgrade purchases (e.g., Year user buying Month plan).
    /// </summary>
    /// <param name="userId">Current user ID</param>
    /// <param name="productId">Target product ID (priceId for Stripe, productId for Apple/Google)</param>
    /// <returns>Validation result with error message if invalid</returns>
    private async Task<(bool IsValid, string? ErrorMessage)> ValidateSubscriptionUpgradePathAsync(
        Guid userId, string productId)
    {
        // Skip validation if ActorFactory is not available
        if (_actorFactory == null)
        {
            _logger.LogWarning(
                "[GodGPTPaymentController][ValidateSubscriptionUpgradePath] ActorFactory not available, skipping validation");
            return (true, null);
        }

        // Get target product configuration
        if (!_productPlanTypes.TryGetValue(productId, out var targetPlanTypeInt))
        {
            _logger.LogWarning(
                "[GodGPTPaymentController][ValidateSubscriptionUpgradePath] ProductId {ProductId} not found in configuration",
                productId);
            // Allow purchase if product not found (configuration issue, not user error)
            return (true, null);
        }

        var targetPlanType = (QuotaPlanType)targetPlanTypeInt;
        var targetIsUltimate = _productIsUltimate.GetValueOrDefault(productId, false);

        try
        {
            // Get user's current subscription via UserQuotaGAgent
            var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId.ToString());
            var userQuotaAgent = userQuotaActor.As<IUserQuotaGAgent>();
            var currentSubscription = await userQuotaAgent.GetSubscriptionAsync(targetIsUltimate);

            // If no active subscription, allow any purchase
            if (!currentSubscription.IsActive)
            {
                _logger.LogInformation(
                    "[GodGPTPaymentController][ValidateSubscriptionUpgradePath] User {UserId} has no active subscription, allowing purchase",
                    userId);
                return (true, null);
            }

            var currentPlanType = (QuotaPlanType)(int)currentSubscription.PlanType;

            _logger.LogInformation(
                "[GodGPTPaymentController][ValidateSubscriptionUpgradePath] Validating: Current={CurrentPlan}, Target={TargetPlan}, UserId={UserId}",
                currentPlanType, targetPlanType, userId);

            // Use IsUpgradeOrSameLevel to allow same-level renewals and upgrades
            // This matches the business logic: users can renew or upgrade, but not downgrade
            if (!SubscriptionHelper.IsUpgradeOrSameLevel(currentPlanType, targetPlanType))
            {
                var currentPlanName = SubscriptionHelper.GetPlanDisplayName(currentPlanType, targetIsUltimate);
                var targetPlanName = SubscriptionHelper.GetPlanDisplayName(targetPlanType, targetIsUltimate);

                _logger.LogWarning(
                    "[GodGPTPaymentController][ValidateSubscriptionUpgradePath] Invalid downgrade: {CurrentPlan} -> {TargetPlan}, UserId={UserId}",
                    currentPlanName, targetPlanName, userId);

                return (false, $"Invalid upgrade path: {currentPlanName} users cannot downgrade to {targetPlanName}. Please wait for your current subscription to expire or contact support.");
            }

            _logger.LogInformation(
                "[GodGPTPaymentController][ValidateSubscriptionUpgradePath] Valid path: {CurrentPlan} -> {TargetPlan}, UserId={UserId}",
                currentPlanType, targetPlanType, userId);

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GodGPTPaymentController][ValidateSubscriptionUpgradePath] Error validating for user {UserId}, allowing purchase",
                userId);
            // On error, allow purchase to avoid blocking legitimate payments
            return (true, null);
        }
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
