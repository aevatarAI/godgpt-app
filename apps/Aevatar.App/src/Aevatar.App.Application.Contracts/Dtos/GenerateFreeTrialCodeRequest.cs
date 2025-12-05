using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Dtos;

public class GenerateFreeTrialCodeRequest
{
    [Required]
    public int TrialDays { get; set; }
    [Required]
    public string ProductId { get; set; }
    public PaymentPlatform Platform { get; set; } = PaymentPlatform.Stripe;
    [Required]
    public DateTime StartTime { get; set; }
    [Required]
    public DateTime EndTime { get; set; }
    public string Description { get; set; } = string.Empty;
    [Required]
    public int Quantity { get; set; }
}