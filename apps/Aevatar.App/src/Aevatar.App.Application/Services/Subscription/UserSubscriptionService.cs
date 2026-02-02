using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.App.Services.Subscription.Options;
using Aevatar.Application.Grains.Subscription;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Payment.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Volo.Abp;
using PaymentPlatform = Aevatar.Agents.GodGPT.Protos.InviteCode.PaymentPlatform;
using PlanType = Aevatar.Agents.GodGPT.Protos.InviteCode.PlanType;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service for querying user subscription information.
/// </summary>
[RemoteService(IsEnabled = false)]
public class UserSubscriptionService : AppAppService, IUserSubscriptionService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IOptionsMonitor<UserSubscriptionOptions> _options;

    public UserSubscriptionService(
        IGAgentActorFactory actorFactory,
        IOptionsMonitor<UserSubscriptionOptions> options )
    {
        _options = options;
        _actorFactory = actorFactory;
    }
    
    private async Task<IUserQuotaGAgent> GetUserQuotaGAgentAsync(string userId)
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId);
            
        return actor.As<IUserQuotaGAgent>();
    }
    
    private async Task<ISubscriptionProductGAgent> GetProductGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<SubscriptionProductGAgent>(SubscriptionGAgentKeys.ProductGAgentKey);
            
        return actor.As<ISubscriptionProductGAgent>();
    }

    private async Task<IPlatformPriceGAgent> GetPriceGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<PlatformPriceGAgent>(SubscriptionGAgentKeys.PriceGAgentKey);
            
        return actor.As<IPlatformPriceGAgent>();
    }

    /// <inheritdoc />
    public async Task<UserSubscriptionDto> GetCurrentSubscriptionAsync(Guid userId)
    {
        var userQuotaGAgent = await GetUserQuotaGAgentAsync(userId.ToString());

        // 1. Get regular subscription info
        var subscriptionInfo = await userQuotaGAgent.GetSubscriptionProtoAsync();

        // 2. Get ultimate subscription info
        var ultimateSubscriptionInfo = await userQuotaGAgent.GetSubscriptionProtoAsync(true);

        // 3. Determine which subscription is active (prefer ultimate if both active)
        bool isUltimate;
        SubscriptionInfoProto? activeSubscription;
        
        if (ultimateSubscriptionInfo?.IsActive == true)
        {
            activeSubscription = ultimateSubscriptionInfo;
            isUltimate = true;
        }
        else if (subscriptionInfo?.IsActive == true)
        {
            activeSubscription = subscriptionInfo;
            isUltimate = false;
        }
        else
        {
            // No active subscription
            return new UserSubscriptionDto
            {
                IsActive = false
            };
        }

        var platformProductId =
            activeSubscription.HasPlatform && (activeSubscription.Platform == (int)PaymentPlatform.GooglePlay ||
            activeSubscription.Platform == (int)PaymentPlatform.AppStore)
                ? activeSubscription.PlatformProductId
                : null;
        var platformPriceId =
            activeSubscription.HasPlatform && activeSubscription.Platform == (int)PaymentPlatform.Stripe
                ? activeSubscription.PlatformProductId
                : null;
        var planType = (PlanType)activeSubscription.PlanType;
        var prices = new List<SubscriptionPriceDto>();
        bool isLegacy = false;

        // 4. Determine if legacy or new subscription and get price info
        if (string.IsNullOrEmpty(platformProductId) && string.IsNullOrEmpty(platformPriceId))
        {
            // 4a. Both IDs are null - this is a legacy subscription
            // Query config by PlanType and IsUltimate
            isLegacy = true;
            var legacyProduct = GetLegacyProductByPlanType(planType, isUltimate, (PaymentPlatform)activeSubscription.Platform);
            prices = legacyProduct?.Prices.Select(p => new SubscriptionPriceDto { Price = p.Price, Currency = p.Currency }).ToList() ?? new List<SubscriptionPriceDto>();
        }
        else
        {
            // 4b. Has platformProductId or platformPriceId
            // First try to find in config
            var legacyProduct = GetLegacyProductByIds(platformProductId, platformPriceId);
            
            if (legacyProduct != null)
            {
                // Found in config - this is a legacy subscription
                isLegacy = true;
                prices = legacyProduct.Prices.Select(p => new SubscriptionPriceDto { Price = p.Price, Currency = p.Currency }).ToList() ?? new List<SubscriptionPriceDto>();
                planType = legacyProduct.PlanType;
                isUltimate = legacyProduct.IsUltimate;
            }
            else
            {
                // Not in config - query from GAgent
                isLegacy = false;
                
                if (!string.IsNullOrEmpty(platformPriceId))
                {
                    // Query price info from PlatformPriceGAgent
                    var priceGAgent = await GetPriceGAgentAsync();

                    var priceInfo = await priceGAgent.GetPriceByPlatformPriceIdAsync(platformPriceId);
                    if (priceInfo != null)
                    {
                        prices.Add(new SubscriptionPriceDto 
                        { 
                            Price = priceInfo.Price, 
                            Currency = priceInfo.Currency 
                        });
                    }
                }
                
                if (!string.IsNullOrEmpty(platformProductId))
                {
                    // Query product info from SubscriptionProductGAgent
                    var productGAgent = await GetProductGAgentAsync();

                    // Get all products and find by platformProductId
                    var allProducts = await productGAgent.GetAllProductsAsync();
                    var productInfo = allProducts.Products?.FirstOrDefault(p => p.PlatformProductId == platformProductId);
                    if (productInfo != null)
                    {
                        planType = productInfo.PlanType;
                        isUltimate = productInfo.IsUltimate;
                    }
                }
            }
        }

        return new UserSubscriptionDto
        {
            IsActive = true,
            IsUltimate = isUltimate,
            PlanType = planType,
            PlatformProductId = platformProductId,
            PlatformPriceId = platformPriceId,
            Prices = prices,
            IsLegacy = isLegacy
        };
    }

    /// <summary>
    /// Gets legacy product info by PlanType and IsUltimate.
    /// </summary>
    private LegacyProductInfo? GetLegacyProductByPlanType(PlanType planType, bool isUltimate, PaymentPlatform platform)
    {
        var legacyProducts = _options.CurrentValue.LegacyProducts;
        return legacyProducts.FirstOrDefault(p =>
            p.PlanType == planType && p.IsUltimate == isUltimate && p.Platform == platform);
    }

    /// <summary>
    /// Gets legacy product info by platformProductId or platformPriceId.
    /// </summary>
    private LegacyProductInfo? GetLegacyProductByIds(string? platformProductId, string? platformPriceId)
    {
        var legacyProducts = _options.CurrentValue.LegacyProducts;
        if (legacyProducts == null)
            return null;

        // First try to match by platformProductId
        if (!string.IsNullOrEmpty(platformProductId))
        {
            var product = legacyProducts.FirstOrDefault(p => p.PlatformProductId == platformProductId);
            if (product != null)
                return product;
        }

        // Then try to match by platformPriceId
        if (!string.IsNullOrEmpty(platformPriceId))
        {
            var product = legacyProducts.FirstOrDefault(p => p.PlatformPriceId == platformPriceId);
            if (product != null)
                return product;
        }

        return null;
    }
}
