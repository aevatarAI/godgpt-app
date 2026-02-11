using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.Payment.Abstractions;
using Microsoft.Extensions.Logging;

using AgentPlatform = Aevatar.Agents.GodGPT.Protos.InviteCode.PaymentPlatform;
using AgentPlanType = Aevatar.Agents.GodGPT.Protos.InviteCode.PlanType;

namespace Aevatar.App.Services.Payment;

/// <summary>
/// GAgent-based product data source.
/// Retrieves products from SubscriptionProductGAgent via ISubscriptionProductService.
/// </summary>
public class GAgentProductDataSource : IProductDataSource
{
    private readonly ISubscriptionProductService _productService;
    private readonly ILogger<GAgentProductDataSource> _logger;

    public GAgentProductDataSource(
        ISubscriptionProductService productService,
        ILogger<GAgentProductDataSource> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    public async Task<List<ProductDto>> GetProductsAsync(
        PaymentPlatform platform, 
        string? currency = null,
        CancellationToken ct = default)
    {
        try
        {
            var agentPlatform = MapToAgentPlatform(platform);
            if (agentPlatform == null)
            {
                _logger.LogDebug("[GAgentProductDataSource] Unsupported platform: {Platform}", platform);
                return new List<ProductDto>();
            }

            var products = await _productService.GetListedProductsWithPriceByPlatformAsync(
                agentPlatform.Value, currency);
            
            if (!products.Any())
            {
                _logger.LogDebug("[GAgentProductDataSource] No products for {Platform}", platform);
                return new List<ProductDto>();
            }

            _logger.LogDebug("[GAgentProductDataSource] Got {Count} products for {Platform}", 
                products.Count, platform);
            
            return products.Select(src => MapToProductDto(src, agentPlatform.Value)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GAgentProductDataSource] Error getting products for {Platform}", platform);
            return new List<ProductDto>();
        }
    }

    public async Task<ProductDto?> GetProductByPlatformProductIdAsync(
        PaymentPlatform platform,
        string productId,
        CancellationToken ct = default)
    {
        try
        {
            var agentPlatform = MapToAgentPlatform(platform);
            if (agentPlatform == null)
            {
                _logger.LogDebug("[GAgentProductDataSource] Unsupported platform for single product: {Platform}", platform);
                return null;
            }

            SubscriptionProductWithPriceDto? product;
            if (platform == PaymentPlatform.Stripe)
                product = await _productService.GetProductByPlatformPriceIdAsync(productId);
            else
                product = await _productService.GetProductByPlatformProductIdAsync(productId, agentPlatform.Value);

            if (product == null)
            {
                _logger.LogDebug("[GAgentProductDataSource] Product not found: Platform={Platform}, ProductId={ProductId}", platform, productId);
                return null;
            }

            return MapToProductDto(product, agentPlatform.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GAgentProductDataSource] Error getting product for {Platform} {ProductId}", platform, productId);
            return null;
        }
    }

    #region Mapping Helpers

    private static AgentPlatform? MapToAgentPlatform(PaymentPlatform platform) => platform switch
    {
        PaymentPlatform.Stripe => AgentPlatform.Stripe,
        PaymentPlatform.AppStore => AgentPlatform.AppStore,
        PaymentPlatform.GooglePlay => AgentPlatform.GooglePlay,
        _ => null
    };

    private static ProductDto MapToProductDto(SubscriptionProductWithPriceDto src, AgentPlatform platform)
    {
        var billingCycle = MapPlanTypeToBillingCycle(src.PlanType);
        var productDto = new ProductDto
        {
            // For Stripe, use PlatformPriceId; for Apple/Google, use PlatformProductId
            ProductId = platform == AgentPlatform.Stripe ? src.PlatformPriceId : src.PlatformProductId,
            Name = src.Name,
            Description = src.Description,
            Price = (decimal)src.Price,
            Currency = src.Currency ?? "USD",
            PlanType = src.IsUltimate ? PlanType.Premium : PlanType.Basic,
            BillingCycle = billingCycle,
            IsActive = true,
            Metadata = new Dictionary<string, string>
            {
                ["isUltimate"] = src.IsUltimate.ToString().ToLower(),
                ["originalPlanType"] = ((int)src.PlanType).ToString(),
                ["dailyAvgPrice"] = CalculateDailyAvgPrice((decimal)src.Price, billingCycle)
            }
        };
        
        if (platform == AgentPlatform.Stripe)
        {
            productDto.Metadata["mode"] = "subscription";
        }
        
        return productDto;
    }

    private static BillingCycle MapPlanTypeToBillingCycle(AgentPlanType planType) => planType switch
    {
        AgentPlanType.Day => BillingCycle.Daily,
        AgentPlanType.Week => BillingCycle.Weekly,
        AgentPlanType.Month => BillingCycle.Monthly,
        AgentPlanType.Year => BillingCycle.Yearly,
        _ => BillingCycle.Monthly
    };

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

    #endregion
}
