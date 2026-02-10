using System;
using System.Threading.Tasks;
using Aevatar.Payment.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Application.Services.Payment;

/// <summary>
/// Ensures business agents (UserQuotaGAgent, InvitationGAgent) are linked
/// as children of PaymentIndexGAgent before payment events are broadcast.
/// Prevents event loss caused by the race condition where events fire
/// before subscription relationships exist.
/// </summary>
public class GodGPTPaymentEventPreHandler : IPaymentEventPreHandler
{
    private readonly PaymentBusinessRegistrationService _registrationService;
    private readonly ILogger<GodGPTPaymentEventPreHandler> _logger;

    public GodGPTPaymentEventPreHandler(
        PaymentBusinessRegistrationService registrationService,
        ILogger<GodGPTPaymentEventPreHandler> logger)
    {
        _registrationService = registrationService;
        _logger = logger;
    }

    public async Task OnBeforePaymentNotificationAsync(Guid userId)
    {
        _logger.LogDebug(
            "[GodGPTPaymentEventPreHandler] Ensuring business agents linked for user {UserId}",
            userId);

        await _registrationService.RegisterBusinessAgentsForUserAsync(userId);
    }
}
