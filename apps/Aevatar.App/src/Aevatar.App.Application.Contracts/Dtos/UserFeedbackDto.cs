using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Dtos;


public class SubmitFeedbackInput
{
    [Required]
    //Feedback type (Cancel/Change)
    public string FeedbackType { get; set; }
    public List<FeedbackReasonEnum> Reasons { get; set; } = new();
    public string Response { get; set; } = string.Empty;
    public bool ContactRequested { get; set; } = false;
    public string Email { get; set; } = string.Empty;
    public bool SkippedFeedback { get; set; } = false;
}

public class TriggerWeeklyReportInput
{
    public string? RecipientEmails { get; set; } = null;
    public DateTime? StartDate { get; set; } = null;
    public DateTime? EndDate { get; set; } = null;
}