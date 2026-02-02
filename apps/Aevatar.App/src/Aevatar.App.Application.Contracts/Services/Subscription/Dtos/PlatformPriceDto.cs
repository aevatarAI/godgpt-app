using System;

namespace Aevatar.App.Services.Subscription.Dtos;

public class PlatformPriceDto
{
    public double Price { get; set; }
    
    public required string Currency { get; set; }
    
    public string? PlatformPriceId { get; set; }
    
    public DateTime LastSyncedAt { get; set; }
}