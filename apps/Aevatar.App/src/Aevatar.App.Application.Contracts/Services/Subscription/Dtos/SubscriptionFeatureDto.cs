using Aevatar.Agents.GodGPT.Protos.Subscription;

namespace Aevatar.App.Services.Subscription.Dtos;

// Subscription feature DTO with localized content
public class SubscriptionFeatureDto
{
    public string Id { get; set; }                        // Guid as string
    
    public string NameKey { get; set; } = string.Empty;   
    
    public string Name { get; set; } = string.Empty;      // Localized feature name
    
    public string? Description { get; set; }              // Localized feature description
    
    public SubscriptionFeatureType Type { get; set; }
    
    public string? TypeName { get; set; }                 // Localized type display name
    
    public int DisplayOrder { get; set; } 
    
    public SubscriptionFeatureUsage Usage { get; set; } 
}