using System.Collections.Generic;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;

namespace Aevatar.App.Services.Subscription.Dtos;

public class SubscriptionProductDto
{
    public string Id { get; set; }
    
    /// <summary>
    /// Raw key.
    /// </summary>
    public string NameKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized product name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Label key.
    /// </summary>
    public string? LabelKey { get; set; }
    
    /// <summary>
    /// Localized label (e.g., "Most Popular").
    /// </summary>
    public string? Label { get; set; }
    
    public PlanType PlanType { get; set; }
    
    /// <summary>
    /// Localized product description.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized highlight/tagline.
    /// </summary>
    public string? Highlight { get; set; }
    
    public bool IsUltimate { get; set; }
    
    public List<SubscriptionFeatureDto> Features { get; set; } = new();
    
    public PaymentPlatform Platform { get; set; }

    public string? PlatformProductId { get; set; }
    
    /// <summary>
    /// Price information (only included for Web platform).
    /// iOS/Android clients should use native SDKs to fetch prices.
    /// </summary>
    public PlatformPriceDto? Price { get; set; }
    
    /// <summary>
    /// Display order for page presentation (lower values appear first).
    /// </summary>
    public int DisplayOrder { get; set; }
}
