namespace Aevatar.Payment.Agents;

/// <summary>
/// Promotion/Discount information
/// </summary>
public class Promotion
{
    /// <summary>Promotion ID</summary>
    public string PromotionId { get; set; } = string.Empty;
    
    /// <summary>Promotion type (coupon/promo_code/trial/etc)</summary>
    public string PromotionType { get; set; } = string.Empty;
    
    /// <summary>Promotion code</summary>
    public string? Code { get; set; }
    
    /// <summary>Display name</summary>
    public string? Name { get; set; }
    
    /// <summary>Amount off in smallest unit</summary>
    public long? AmountOff { get; set; }
    
    /// <summary>Percent off (0-100)</summary>
    public int? PercentOff { get; set; }
    
    /// <summary>Additional metadata</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

