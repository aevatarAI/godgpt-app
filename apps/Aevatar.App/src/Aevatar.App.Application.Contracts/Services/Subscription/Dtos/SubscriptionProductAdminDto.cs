using System;
using System.Collections.Generic;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;

namespace Aevatar.App.Services.Subscription.Dtos;

/// <summary>
/// Admin view of subscription product with raw keys and localized values.
/// </summary>
public class SubscriptionProductAdminDto
{
    public string Id { get; set; }
    public string NameKey { get; set; } = string.Empty;
    /// <summary>
    /// Localized name resolved from NameKey.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    public string? LabelId { get; set; }
    public SubscriptionLabelDto? Label { get; set; }
    public PlanType PlanType { get; set; }
    public string DescriptionKey { get; set; } = string.Empty;
    /// <summary>
    /// Localized description resolved from DescriptionKey.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    public string? HighlightKey { get; set; }
    /// <summary>
    /// Localized highlight resolved from HighlightKey.
    /// </summary>
    public string? Highlight { get; set; }
    public bool IsUltimate { get; set; }
    public List<string> FeatureIds { get; set; } = new();
    public List<SubscriptionFeatureDto> Features { get; set; } = new();
    public string PlatformProductId { get; set; } = string.Empty;
    public PaymentPlatform Platform { get; set; }
    public bool? IsListed { get; set; }
    /// <summary>
    /// Display order for sorting products.
    /// </summary>
    public int DisplayOrder { get; set; }
    public List<PlatformPriceDto> Prices { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

