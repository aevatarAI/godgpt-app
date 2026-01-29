using System.Collections.Generic;
using Aevatar.Agents.GodGPT.Protos.InviteCode;

namespace Aevatar.App.Services.Subscription.Dtos;

/// <summary>
/// DTO for user's current subscription information.
/// </summary>
public class UserSubscriptionDto
{
    /// <summary>
    /// Whether the user has an active subscription.
    /// </summary>
    public bool IsActive { get; set; }
    
    /// <summary>
    /// Whether the subscription is an ultimate plan.
    /// </summary>
    public bool IsUltimate { get; set; }
    
    /// <summary>
    /// The plan type (e.g., Monthly, Yearly).
    /// Null if no active subscription.
    /// </summary>
    public PlanType? PlanType { get; set; }
    
    /// <summary>
    /// The platform product ID (e.g., Stripe product ID, Apple product ID).
    /// Null if no active subscription or legacy subscription.
    /// </summary>
    public string? PlatformProductId { get; set; }
    
    /// <summary>
    /// The platform price ID (e.g., Stripe price ID).
    /// Null if no active subscription or legacy subscription.
    /// </summary>
    public string? PlatformPriceId { get; set; }
    
    /// <summary>
    /// List of prices for different currencies.
    /// Empty if no active subscription or price not available.
    /// </summary>
    public List<SubscriptionPriceDto> Prices { get; set; } = new();
    
    /// <summary>
    /// Whether this is a legacy subscription 
    /// Legacy subscriptions exist before the new subscription product management system.
    /// </summary>
    public bool IsLegacy { get; set; }
}

/// <summary>
/// Price information for a specific currency.
/// </summary>
public class SubscriptionPriceDto
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
