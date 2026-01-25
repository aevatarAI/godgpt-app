using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service for subscription products.
/// </summary>
[RemoteService(IsEnabled = false)]
public class SubscriptionProductService : AppAppService, ISubscriptionProductService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<SubscriptionProductService> _logger;

    public SubscriptionProductService(IGAgentActorFactory actorFactory,
        ILogger<SubscriptionProductService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    #region User-facing queries

    public async Task<List<SubscriptionProductDto>> GetListedProductsByPlatformAsync(
        PaymentPlatform platform,
        string? currency = null)
    {
        var productGAgent = await GetProductGAgentAsync();
        var productsResponse = await productGAgent.GetListedProductsByPlatformAsync(platform);
        var products = productsResponse.Products.OrderBy(p => p.DisplayOrder).ToList();

        var result = new List<SubscriptionProductDto>();
        foreach (var product in products)
        {
            var dto = MapToProductDto(product);

            // Enrich with features
            if (product.FeatureIds.Any())
            {
                var featureGAgent = await GetFeatureGAgentAsync();
                var subscriptionFeatureIdList = new SubscriptionFeatureIdList();
                subscriptionFeatureIdList.FeatureIds.AddRange(product.FeatureIds);
                var featureList = await featureGAgent.GetFeaturesByIdsAsync(subscriptionFeatureIdList);
                dto.Features = featureList.Features.Select(MapToFeatureDto).ToList();
            }

            // Enrich with label
            if (product.HasLabelId)
            {
                var labelGAgent = await GetLabelGAgentAsync();
                var label = await labelGAgent.GetLabelAsync(product.LabelId);
                if (label != null)
                {
                    dto.LabelKey = label.NameKey;
                    dto.Label = L[$"{label.NameKey}"];
                }
            }

            // Enrich with price (only for Stripe)
            if (platform == PaymentPlatform.Stripe)
            {
                var priceGAgent = await GetPriceGAgentAsync();
                var priceList = await priceGAgent.GetPricesByProductIdAsync(product.Id);
                dto.Price = SelectPrice(priceList.Prices, currency);
            }

            result.Add(dto);
        }

        return result;
    }

    #endregion

    #region Interl queries

    public async Task<List<SubscriptionProductWithPriceDto>> GetListedProductsWithPriceByPlatformAsync(
        PaymentPlatform platform,
        string? currency = null)
    {
        var productGAgent = await GetProductGAgentAsync();
        var productsResponse = await productGAgent.GetListedProductsByPlatformAsync(platform);
        var products = productsResponse.Products.OrderBy(p => p.DisplayOrder).ToList();

        var result = new List<SubscriptionProductWithPriceDto>();
        var priceGAgent = await GetPriceGAgentAsync();

        foreach (var product in products)
        {
            var dto = new SubscriptionProductWithPriceDto
            {
                PlanType = product.PlanType,
                IsUltimate = product.IsUltimate,
                Description = L[product.DescriptionKey],
                Name = L[product.NameKey],
                PlatformProductId = product.PlatformProductId
            };

            // Enrich with price
            var priceList = await priceGAgent.GetPricesByProductIdAsync(product.Id);
            var price = SelectPlatformPrice(priceList.Prices, currency);
            if (price != null)
            {
                dto.Currency = price.Currency;
                dto.Price = price.Price;
                dto.PlatformPriceId = price.PlatformPriceId;
            }

            result.Add(dto);
        }

        return result;
    }
    
    public async Task<SubscriptionProductWithPriceDto?> GetProductByPlatformProductIdAsync(string platformProductId, PaymentPlatform platform)
    {
        var productGAgent = await GetProductGAgentAsync();
        var subscriptionProduct = await productGAgent.GetProductByPlatformProductIdAsync(platformProductId, platform);
        if (subscriptionProduct == null)
        {
            _logger.LogDebug(
                "[SubscriptionProductService][GetProductByPlatformProductIdAsync] ProductId: {ProductId}. Product not found.",
                platformProductId);
            return null;
        }
        _logger.LogInformation(
            "[SubscriptionProductService][GetProductByPlatformProductIdAsync] Found product with ProductId: {ProductId}, planType: {PlanType}",
            subscriptionProduct.PlatformProductId, subscriptionProduct.PlanType);

        var dto = new SubscriptionProductWithPriceDto
        {
            PlanType = subscriptionProduct.PlanType,
            IsUltimate = subscriptionProduct.IsUltimate,
            Description = L[subscriptionProduct.DescriptionKey],
            Name = L[subscriptionProduct.NameKey],
            PlatformProductId = subscriptionProduct.PlatformProductId
        };
        
        var priceGAgent = await GetPriceGAgentAsync();
        var platformPriceList = await priceGAgent.GetPricesByProductIdAsync(subscriptionProduct.Id);

        var price = platformPriceList.Prices.FirstOrDefault();
        if (price != null)
        {
            dto.Currency = price.Currency;
            dto.Price = price.Price;
            dto.PlatformPriceId = price.PlatformPriceId;
        }
        else
        {
            _logger.LogDebug(
                "[SubscriptionProductService][GetProductByPlatformProductIdAsync] ProductId: {ProductId}. Platform price not found.",
                subscriptionProduct.Id);
        }

        return dto;
    }

    public async Task<SubscriptionProductWithPriceDto?> GetProductByPlatformPriceIdAsync(string platformPriceId)
    {
        var priceGAgent = await GetPriceGAgentAsync();
        var platformPrice =  await priceGAgent.GetPriceByPlatformPriceIdAsync(platformPriceId);

        if (platformPrice == null)
        {
            _logger.LogDebug(
                "[SubscriptionProductService][GetProductByPlatformPriceIdAsync] PriceId: {PriceId}. Platform price not found.",
                platformPriceId);
            return null;
        }

        var productGAgent = await GetProductGAgentAsync();
        var subscriptionProduct = await productGAgent.GetProductAsync(platformPrice.ProductId);

        if(subscriptionProduct == null){
            _logger.LogDebug(
                "[SubscriptionProductService][GetProductByPlatformPriceIdAsync] ProductId: {ProductId}. Product not found.",
                platformPrice.ProductId);
            return null;
        }

        _logger.LogDebug(
            "[SubscriptionProductService][GetProductByPlatformPriceIdAsync] Found product with priceId: {PriceId}, planType: {PlanType}, amount: {Amount} {Currency}",
            platformPrice.Id, subscriptionProduct.PlanType, platformPrice.Price, platformPrice.Currency);

        return new SubscriptionProductWithPriceDto{
            PlatformPriceId = platformPrice.PlatformPriceId,
            PlanType = subscriptionProduct.PlanType,
            Price = platformPrice.Price,
            Currency = platformPrice.Currency,
            IsUltimate = subscriptionProduct.IsUltimate,
            Description = L[subscriptionProduct.DescriptionKey],
            Name = L[subscriptionProduct.NameKey],
            PlatformProductId = subscriptionProduct.PlatformProductId
        };
    }

    #endregion

    #region Admin queries

    public async Task<List<SubscriptionProductAdminDto>> GetAllProductsAdminAsync(
        PaymentPlatform? platform = null,
        bool? isListed = null)
    {
        var productGAgent = await GetProductGAgentAsync();
        var productList = await productGAgent.GetAllProductsAsync();
        var products = productList.Products.ToList();

        if (platform.HasValue)
            products = products.Where(p => p.Platform == platform.Value).ToList();

        if (isListed.HasValue)
            products = products.Where(p => p.IsListed == isListed.Value).ToList();

        var result = new List<SubscriptionProductAdminDto>();
        foreach (var product in products)
        {
            var dto = await BuildProductAdminDtoAsync(product);
            result.Add(dto);
        }

        return result;
    }

    public async Task<SubscriptionProductAdminDto?> GetProductAdminAsync(string productId)
    {
        var productGAgent = await GetProductGAgentAsync();
        var product = await productGAgent.GetProductAsync(productId);
        if (product == null) return null;

        return await BuildProductAdminDtoAsync(product);
    }

    #endregion

    #region Admin mutations

    public async Task<SubscriptionProductAdminDto> CreateProductAsync(CreateProductDto dto)
    {
        var productGAgent = await GetProductGAgentAsync();
        await productGAgent.CreateProductAsync(dto);

        var product = await productGAgent.GetProductByPlatformProductIdAsync(
            dto.PlatformProductId, dto.Platform);

        return await BuildProductAdminDtoAsync(product!);
    }

    public async Task<SubscriptionProductAdminDto> UpdateProductAsync(string productId, UpdateProductDto dto)
    {
        var productGAgent = await GetProductGAgentAsync();
        await productGAgent.UpdateProductAsync(productId, dto);

        var product = await productGAgent.GetProductAsync(productId);
        return await BuildProductAdminDtoAsync(product!);
    }

    public async Task DeleteProductAsync(string productId)
    {
        var productGAgent = await GetProductGAgentAsync();
        await productGAgent.DeleteProductAsync(productId);
    }

    public async Task<SubscriptionProductAdminDto> SetProductListedAsync(string productId, bool isListed)
    {
        var productGAgent = await GetProductGAgentAsync();
        await productGAgent.SetProductListedAsync(productId, isListed);

        var product = await productGAgent.GetProductAsync(productId);
        return await BuildProductAdminDtoAsync(product!);
    }

    #endregion

    #region Price management (Product-scoped price operations)

    public async Task<PlatformPriceDto> SetPriceAsync(string productId, SetPriceDto dto)
    {
        var priceGAgent = await GetPriceGAgentAsync();
        var price = await priceGAgent.SetPriceAsync(productId, dto);
        return new PlatformPriceDto
        {
            Currency = price.Currency,
            PlatformPriceId = price.PlatformPriceId,
            LastSyncedAt = price.LastSyncedAt.ToDateTime(),
            Price = price.Price
        };
    }

    public async Task DeletePriceAsync(string productId, PaymentPlatform platform, string currency)
    {
        var priceGAgent = await GetPriceGAgentAsync();
        await priceGAgent.DeletePriceAsync(productId, platform, currency);
    }

    #endregion

    #region GAgent
    
    private async Task<ISubscriptionProductGAgent> GetProductGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<SubscriptionProductGAgent>(SubscriptionGAgentKeys.ProductGAgentKey);
            
        return actor.As<ISubscriptionProductGAgent>();
    }

    
    private async Task<ISubscriptionLabelGAgent> GetLabelGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<SubscriptionLabelGAgent>(SubscriptionGAgentKeys.LabelGAgentKey);
            
        return actor.As<ISubscriptionLabelGAgent>();
    }
    
    private async Task<ISubscriptionFeatureGAgent> GetFeatureGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<SubscriptionFeatureGAgent>(SubscriptionGAgentKeys.FeatureGAgentKey);
            
        return actor.As<ISubscriptionFeatureGAgent>();
    }
    
    private async Task<IPlatformPriceGAgent> GetPriceGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<PlatformPriceGAgent>(SubscriptionGAgentKeys.PriceGAgentKey);
            
        return actor.As<IPlatformPriceGAgent>();
    }

    #endregion

    #region DTO Building (Data enrichment + Mapping)

    /// <summary>
    /// Builds a complete admin DTO by fetching related data and mapping.
    /// This separates "data enrichment" from "pure mapping".
    /// </summary>
    private async Task<SubscriptionProductAdminDto> BuildProductAdminDtoAsync(SubscriptionProduct product)
    {
        // 1. Map basic product fields
        var dto = MapToProductAdminDto(product);

        // 2. Enrich with features
        if (product.FeatureIds.Any())
        {
            var featureGAgent = await GetFeatureGAgentAsync();
            var subscriptionFeatureIdList = new SubscriptionFeatureIdList();
            subscriptionFeatureIdList.FeatureIds.AddRange(product.FeatureIds);
            var featureList = await featureGAgent.GetFeaturesByIdsAsync(subscriptionFeatureIdList);
            dto.Features = featureList.Features.Select(MapToFeatureDto).ToList();
        }

        // 3. Enrich with label
        if (product.HasLabelId)
        {
            var labelGAgent = await GetLabelGAgentAsync();
            var label = await labelGAgent.GetLabelAsync(product.LabelId);
            if (label != null)
            {
                dto.Label = MapToLabelDto(label);
            }
        }

        // 4. Enrich with prices
        var priceGAgent = await GetPriceGAgentAsync();
        var priceList = await priceGAgent.GetPricesByProductIdAsync(product.Id);
        dto.Prices = priceList.Prices.Select(MapToPriceDto).ToList();

        return dto;
    }

    #endregion

    #region Pure Mapping Methods (No async, no data fetching)

    private string GetFeatureTypeName(SubscriptionFeatureType type) => type switch
    {
        SubscriptionFeatureType.None => L["subscription.feature.type.none"],
        SubscriptionFeatureType.Core => L["subscription.feature.type.core"],
        SubscriptionFeatureType.Advanced => L["subscription.feature.type.advanced"],
        _ => type.ToString()
    };

    private SubscriptionProductDto MapToProductDto(SubscriptionProduct product) => new()
    {
        Id = product.Id,
        NameKey = product.NameKey,
        Name = L[product.NameKey],
        PlanType = product.PlanType,
        Description = L[product.DescriptionKey],
        Highlight = (product.HighlightKey != null ? L[product.HighlightKey] : null) ?? string.Empty,
        IsUltimate = product.IsUltimate,
        Platform = product.Platform,
        PlatformProductId = product.PlatformProductId,
        DisplayOrder = product.DisplayOrder
    };

    private SubscriptionProductAdminDto MapToProductAdminDto(SubscriptionProduct product) => new()
    {
        Id = product.Id,
        NameKey = product.NameKey,
        Name = L[product.NameKey],
        LabelId = product.LabelId,
        PlanType = product.PlanType,
        DescriptionKey = product.DescriptionKey,
        Description = L[product.DescriptionKey],
        HighlightKey = product.HighlightKey,
        Highlight = (product.HasHighlightKey ? L[product.HighlightKey] : null) ?? string.Empty,
        IsUltimate = product.IsUltimate,
        FeatureIds = product.FeatureIds.ToList(),
        PlatformProductId = product.PlatformProductId,
        Platform = product.Platform,
        IsListed = product.HasIsListed ? product.IsListed : null,
        DisplayOrder = product.DisplayOrder,
        CreatedAt = product.CreatedAt.ToDateTime(),
        UpdatedAt = product.UpdatedAt?.ToDateTime()
    };

    private SubscriptionFeatureDto MapToFeatureDto(SubscriptionFeature feature) => new()
    {
        Id = feature.Id,
        NameKey = feature.NameKey,
        Name = L[feature.NameKey],
        Description = (feature.DescriptionKey != null ? L[feature.DescriptionKey] : null) ?? string.Empty,
        Type = feature.Type,
        TypeName = GetFeatureTypeName(feature.Type),
        Usage = feature.Usage,
        DisplayOrder = feature.DisplayOrder
    };

    private SubscriptionLabelDto MapToLabelDto(SubscriptionLabel label) => new()
    {
        Id = label.Id,
        NameKey = label.NameKey,
        Name = L[$"{label.NameKey}"],
        CreatedAt = label.CreatedAt.ToDateTime()
    };

    private PlatformPriceDto MapToPriceDto(PlatformPrice price) => new()
    {
        Price = price.Price,
        Currency = price.Currency,
        PlatformPriceId = price.PlatformPriceId,
        LastSyncedAt = price.LastSyncedAt.ToDateTime()
    };

    private PlatformPriceDto? SelectPrice(IEnumerable<PlatformPrice> prices, string? preferredCurrency)
    {
        var price = SelectPlatformPrice(prices, preferredCurrency);
        return price != null ? MapToPriceDto(price) : null;
    }

    private static PlatformPrice? SelectPlatformPrice(IEnumerable<PlatformPrice> prices, string? preferredCurrency)
    {
        var priceList = prices.ToList();
        if (!priceList.Any()) return null;

        // 1. Try user's preferred currency
        if (!string.IsNullOrEmpty(preferredCurrency))
        {
            var preferred = priceList.FirstOrDefault(p =>
                p.Currency.Equals(preferredCurrency, StringComparison.OrdinalIgnoreCase));
            if (preferred != null) return preferred;
        }

        // 2. Default to USD
        var defaultPrice = priceList.FirstOrDefault(p =>
            p.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase));
        if (defaultPrice != null) return defaultPrice;

        // 3. Final fallback: first available
        return priceList.First();
    }

    #endregion
}
