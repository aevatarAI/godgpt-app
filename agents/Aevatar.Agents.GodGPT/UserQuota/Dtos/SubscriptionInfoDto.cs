using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.UserQuota;

/// <summary>
/// DTO for subscription information
/// Used by UserQuotaGAgent interface methods
/// </summary>
public class SubscriptionInfoDto
{
    public bool IsActive { get; set; }
    public PlanType PlanType { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<string> SubscriptionIds { get; set; } = new();
    public List<string> InvoiceIds { get; set; } = new();
}

