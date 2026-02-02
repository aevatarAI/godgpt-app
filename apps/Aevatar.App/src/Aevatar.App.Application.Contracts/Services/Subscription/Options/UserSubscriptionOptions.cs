using System.Collections.Generic;
using Aevatar.Agents.GodGPT.Protos.InviteCode;

namespace Aevatar.App.Services.Subscription.Options;

/// <summary>
/// Configuration options for user subscription service.
/// </summary>
public class UserSubscriptionOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "UserSubscription";

    /// <summary>
    /// List of legacy products that exist in configuration but not in the new product catalog.
    /// These are subscriptions created before the new subscription product management system.
    /// </summary>
    public List<LegacyProductInfo> LegacyProducts { get; set; } = new();
}

/// <summary>
/// Legacy product information stored in configuration.
/// </summary>
public class LegacyProductInfo
{
    /// <summary>
    /// Platform product ID (e.g., Stripe product ID, Apple product ID).
    /// </summary>
    public string PlatformProductId { get; set; } = string.Empty;

    /// <summary>
    /// Platform price ID (e.g., Stripe price ID).
    /// </summary>
    public string? PlatformPriceId { get; set; }
    
    public PaymentPlatform Platform { get; set; }

    /// <summary>
    /// The plan type (Weekly, Monthly, Yearly).
    /// </summary>
    public PlanType PlanType { get; set; }

    /// <summary>
    /// Whether this is an ultimate subscription.
    /// </summary>
    public bool IsUltimate { get; set; }

    /// <summary>
    /// List of prices for different currencies.
    /// </summary>
    public List<LegacyPriceInfo> Prices { get; set; } = new();
}

/// <summary>
/// Price information for a specific currency.
/// </summary>
public class LegacyPriceInfo
{
    /// <summary>
    /// The subscription price.
    /// </summary>
    public double Price { get; set; }

    /// <summary>
    /// The currency code (e.g., "USD", "CNY").
    /// </summary>
    public string Currency { get; set; } = "USD";
}
