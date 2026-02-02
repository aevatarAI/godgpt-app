using Aevatar.Agents.GodGPT.Protos.InviteCode;

namespace Aevatar.App.Services.Subscription.Dtos;

public class SubscriptionProductWithPriceDto
{
    public string PlatformProductId { get; set; }
    
    public string PlatformPriceId { get; set; }

    public PlanType PlanType { get; set; }
   
    public double Price { get; set; }
    
    public string Currency { get; set; }
    
    public bool IsUltimate { get; set; } 
    
    public string Description { get; set; }

    public string Name { get; set; }
}