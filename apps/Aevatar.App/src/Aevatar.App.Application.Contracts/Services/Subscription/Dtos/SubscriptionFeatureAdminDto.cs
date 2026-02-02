using System;
using Aevatar.Agents.GodGPT.Protos.Subscription;

namespace Aevatar.App.Services.Subscription.Dtos;

/// <summary>
/// Admin view of subscription feature with raw keys.
/// </summary>
public class SubscriptionFeatureAdminDto
{
    public string Id { get; set; }
    public string NameKey { get; set; } = string.Empty;
    public string? DescriptionKey { get; set; }
    
    /// <summary>
    /// Localized feature name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized feature description.
    /// </summary>
    public string? Description { get; set; }
    
    public SubscriptionFeatureType Type { get; set; }
    
    /// <summary>
    /// Localized type display name.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;
    
    /// <summary>
    /// Feature usage configuration.
    /// </summary>
    public SubscriptionFeatureUsage? Usage { get; set; }
    
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
