using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Payment.Agents;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Application.Services.Payment;

/// <summary>
/// Service to register business agents as children of PaymentIndexGAgent
/// This enables business agents to receive payment events via event broadcasting
/// </summary>
public class PaymentBusinessRegistrationService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<PaymentBusinessRegistrationService> _logger;

    public PaymentBusinessRegistrationService(
        IGAgentActorFactory actorFactory,
        ILogger<PaymentBusinessRegistrationService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    /// <summary>
    /// Register business agents for a user as children of PaymentIndexGAgent
    /// This should be called when user first creates a subscription or on app startup
    /// </summary>
    public async Task RegisterBusinessAgentsForUserAsync(Guid userId)
    {
        try
        {
            _logger.LogInformation(
                "[PaymentBusinessRegistrationService] Registering business agents for user {UserId}",
                userId);

            var userIdStr = userId.ToString();

            // Get PaymentIndexGAgent (parent)
            var paymentIndexActor = await _actorFactory.CreateGAgentActorAsync<PaymentIndexGAgent>(userIdStr);

            // Register UserQuotaGAgent
            // UserQuotaGAgent handles: subscription updates, trial code marking, 
            // old subscription cancellation, and Ultimate upgrade processing
            var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userIdStr);
            await ActorHierarchyCoordinator.LinkAsync(paymentIndexActor, userQuotaActor, _logger);
            _logger.LogDebug(
                "[PaymentBusinessRegistrationService] Registered UserQuotaGAgent for user {UserId}",
                userId);

            // Register InvitationGAgent (for inviter rewards)
            // Note: This is registered for the inviter, not the payer
            // The inviter's InvitationGAgent will receive events when their invitees pay
            var invitationActor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(userIdStr);
            await ActorHierarchyCoordinator.LinkAsync(paymentIndexActor, invitationActor, _logger);
            _logger.LogDebug(
                "[PaymentBusinessRegistrationService] Registered InvitationGAgent for user {UserId}",
                userId);

            _logger.LogInformation(
                "[PaymentBusinessRegistrationService] Successfully registered all business agents for user {UserId}",
                userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PaymentBusinessRegistrationService] Failed to register business agents for user {UserId}",
                userId);
            throw;
        }
    }

    /// <summary>
    /// Register business agents for invitee's inviter
    /// This ensures inviter receives rewards when invitee pays
    /// </summary>
    public async Task RegisterInviterBusinessAgentsAsync(Guid inviterId)
    {
        try
        {
            _logger.LogInformation(
                "[PaymentBusinessRegistrationService] Registering business agents for inviter {InviterId}",
                inviterId);

            var inviterIdStr = inviterId.ToString();

            // Get PaymentIndexGAgent (parent)
            var paymentIndexActor = await _actorFactory.CreateGAgentActorAsync<PaymentIndexGAgent>(inviterIdStr);

            // Register InvitationGAgent for inviter (to receive invitee payment events)
            var invitationActor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(inviterIdStr);
            await ActorHierarchyCoordinator.LinkAsync(paymentIndexActor, invitationActor, _logger);
            _logger.LogDebug(
                "[PaymentBusinessRegistrationService] Registered InvitationGAgent for inviter {InviterId}",
                inviterId);

            _logger.LogInformation(
                "[PaymentBusinessRegistrationService] Successfully registered inviter business agents for {InviterId}",
                inviterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PaymentBusinessRegistrationService] Failed to register inviter business agents for {InviterId}",
                inviterId);
            // Don't throw - this is not critical
        }
    }
}
