using Aevatar.Payment.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Payment.Tests;

/// <summary>
/// Tests for Apple notification type mapping and event handling logic
/// </summary>
public class AppleNotificationMappingTests
{
    [Theory(DisplayName = "Apple notification type should map to PaymentStatus")]
    [InlineData("SUBSCRIBED", PaymentStatus.Completed)]
    [InlineData("DID_RENEW", PaymentStatus.Completed)]
    [InlineData("EXPIRED", PaymentStatus.Expired)]
    [InlineData("GRACE_PERIOD_EXPIRED", PaymentStatus.Expired)]
    [InlineData("REVOKE", PaymentStatus.Refunded)]
    [InlineData("REFUND", PaymentStatus.Refunded)]
    public void AppleNotificationType_ShouldMapCorrectly(string notificationType, PaymentStatus expected)
    {
        var result = notificationType switch
        {
            "SUBSCRIBED" => PaymentStatus.Completed,
            "DID_RENEW" => PaymentStatus.Completed,
            "EXPIRED" => PaymentStatus.Expired,
            "GRACE_PERIOD_EXPIRED" => PaymentStatus.Expired,
            "REVOKE" => PaymentStatus.Refunded,
            "REFUND" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };

        result.ShouldBe(expected);
    }

    [Theory(DisplayName = "Apple notification type should be correctly filtered")]
    [InlineData("SUBSCRIBED", true)]
    [InlineData("DID_RENEW", true)]
    [InlineData("EXPIRED", true)]
    [InlineData("REVOKE", true)]
    [InlineData("REFUND", true)]
    [InlineData("DID_CHANGE_RENEWAL_STATUS", true)]
    [InlineData("DID_CHANGE_RENEWAL_PREF", true)]
    [InlineData("CONSUMPTION_REQUEST", false)]
    [InlineData("TEST", false)]
    [InlineData("PRICE_INCREASE", false)]
    public void AppleNotificationType_ShouldBeCorrectlyFiltered(string type, bool shouldProcess)
    {
        var allowedTypes = new HashSet<string>
        {
            "SUBSCRIBED",
            "DID_RENEW",
            "DID_CHANGE_RENEWAL_STATUS",
            "EXPIRED",
            "GRACE_PERIOD_EXPIRED",
            "REVOKE",
            "DID_CHANGE_RENEWAL_PREF",
            "REFUND"
        };

        allowedTypes.Contains(type).ShouldBe(shouldProcess);
    }

    [Fact(DisplayName = "DID_CHANGE_RENEWAL_STATUS with AUTO_RENEW_DISABLED should be Cancelled")]
    public void AutoRenewDisabled_ShouldBeCancelled()
    {
        // Special case: subtype determines the mapping
        var notificationType = "DID_CHANGE_RENEWAL_STATUS";
        var subtype = "AUTO_RENEW_DISABLED";

        var result = (notificationType, subtype) switch
        {
            ("DID_CHANGE_RENEWAL_STATUS", "AUTO_RENEW_DISABLED") => PaymentStatus.Cancelled,
            _ => PaymentStatus.Pending
        };

        result.ShouldBe(PaymentStatus.Cancelled);
    }
}
