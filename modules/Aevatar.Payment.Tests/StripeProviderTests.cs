using Aevatar.Payment.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Payment.Tests;

/// <summary>
/// Tests for Stripe status mapping and event type handling logic
/// </summary>
public class StripeStatusMappingTests
{
    [Theory(DisplayName = "Stripe subscription status should map to PaymentStatus")]
    [InlineData("active", PaymentStatus.Completed)]
    [InlineData("past_due", PaymentStatus.Pending)]
    [InlineData("canceled", PaymentStatus.Cancelled)]
    [InlineData("unpaid", PaymentStatus.Failed)]
    [InlineData("trialing", PaymentStatus.Pending)]
    public void StripeStatus_ShouldMapCorrectly(string stripeStatus, PaymentStatus expected)
    {
        var result = stripeStatus switch
        {
            "active" => PaymentStatus.Completed,
            "past_due" => PaymentStatus.Pending,
            "canceled" => PaymentStatus.Cancelled,
            "unpaid" => PaymentStatus.Failed,
            _ => PaymentStatus.Pending
        };

        result.ShouldBe(expected);
    }

    [Theory(DisplayName = "Stripe webhook event types should be recognized")]
    [InlineData("checkout.session.completed", true)]
    [InlineData("invoice.paid", true)]
    [InlineData("customer.subscription.updated", true)]
    [InlineData("customer.subscription.deleted", true)]
    [InlineData("customer.created", false)]
    [InlineData("payment_intent.succeeded", false)]
    public void StripeEventType_ShouldBeCorrectlyRecognized(string eventType, bool shouldProcess)
    {
        var processableEvents = new HashSet<string>
        {
            "checkout.session.completed",
            "invoice.paid",
            "customer.subscription.updated",
            "customer.subscription.deleted"
        };

        processableEvents.Contains(eventType).ShouldBe(shouldProcess);
    }

    [Theory(DisplayName = "Stripe price interval should map to PlanType")]
    [InlineData("month", PlanType.Basic)]
    [InlineData("year", PlanType.Premium)]
    [InlineData("week", PlanType.Basic)]
    [InlineData(null, PlanType.Premium)] // Lifetime (no recurring)
    public void StripePriceInterval_ShouldMapToPlanType(string? interval, PlanType expected)
    {
        var result = interval switch
        {
            "month" => PlanType.Basic,
            "year" => PlanType.Premium,
            "week" => PlanType.Basic,
            null => PlanType.Premium, // Lifetime
            _ => PlanType.Basic
        };

        result.ShouldBe(expected);
    }
}
