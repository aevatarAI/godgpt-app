using System;

namespace Aevatar.App.Services.Subscription.Dtos;

public class SubscriptionLabelDto
{
    public string Id { get; set; }                       // Guid as string
    
    public string NameKey { get; set; }

    public string Name { get; set; } = string.Empty;    // Localized label name
    
    public DateTime CreatedAt { get; set; }
}