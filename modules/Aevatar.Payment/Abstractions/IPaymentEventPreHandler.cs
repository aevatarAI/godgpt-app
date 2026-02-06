namespace Aevatar.Payment.Abstractions;

/// <summary>
/// Hook executed before PaymentIndexGAgent broadcasts events (Direction.Down).
/// Ensures business agent subscriptions (parent-child links) are established
/// so children receive PaymentCompleted/Cancelled/Refund events.
/// </summary>
public interface IPaymentEventPreHandler
{
    /// <summary>
    /// Called before payment events are broadcast. Implementations should be idempotent.
    /// </summary>
    Task OnBeforePaymentNotificationAsync(Guid userId);
}
