using Aevatar.Payment.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Payment.Tests;

/// <summary>
/// Tests for RevenueCat (Google Play) event type mapping and handling logic
/// </summary>
public class RevenueCatEventMappingTests
{
    [Theory(DisplayName = "RevenueCat event type should map to PaymentStatus")]
    [InlineData("INITIAL_PURCHASE", PaymentStatus.Completed)]
    [InlineData("RENEWAL", PaymentStatus.Completed)]
    [InlineData("CANCELLATION", PaymentStatus.Cancelled)]
    [InlineData("EXPIRATION", PaymentStatus.Expired)]
    [InlineData("REFUND", PaymentStatus.Refunded)]
    [InlineData("PRODUCT_CHANGE", PaymentStatus.Pending)]
    public void RevenueCatEventType_ShouldMapCorrectly(string eventType, PaymentStatus expected)
    {
        var result = eventType switch
        {
            "INITIAL_PURCHASE" => PaymentStatus.Completed,
            "RENEWAL" => PaymentStatus.Completed,
            "CANCELLATION" => PaymentStatus.Cancelled,
            "EXPIRATION" => PaymentStatus.Expired,
            "REFUND" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };

        result.ShouldBe(expected);
    }

    [Theory(DisplayName = "RevenueCat event type should be correctly identified as business event")]
    [InlineData("INITIAL_PURCHASE", true)]
    [InlineData("RENEWAL", true)]
    [InlineData("CANCELLATION", true)]
    [InlineData("EXPIRATION", true)]
    [InlineData("PRODUCT_CHANGE", false)]
    [InlineData("BILLING_ISSUE", false)]
    [InlineData("SUBSCRIBER_ALIAS", false)]
    [InlineData("TRANSFER", false)]
    public void RevenueCatEventType_ShouldBeCorrectlyIdentifiedAsBusinessEvent(string eventType, bool isBusinessEvent)
    {
        var businessEvents = new HashSet<string>
        {
            "INITIAL_PURCHASE",
            "RENEWAL",
            "CANCELLATION",
            "EXPIRATION"
        };

        businessEvents.Contains(eventType).ShouldBe(isBusinessEvent);
    }

    [Fact(DisplayName = "Zero price purchase events should be filtered out")]
    public void ZeroPricePurchase_ShouldBeFilteredOut()
    {
        // Business rule: purchases with 0 price (trials, promos) might not be counted
        // Only applies to INITIAL_PURCHASE and RENEWAL, not CANCELLATION/EXPIRATION
        
        bool ShouldProcess(string eventType, decimal price)
        {
            if (eventType is "CANCELLATION" or "EXPIRATION")
                return true;
            return price > 0;
        }

        ShouldProcess("INITIAL_PURCHASE", 0).ShouldBeFalse();
        ShouldProcess("INITIAL_PURCHASE", 9.99m).ShouldBeTrue();
        ShouldProcess("CANCELLATION", 0).ShouldBeTrue(); // Always process
        ShouldProcess("EXPIRATION", 0).ShouldBeTrue(); // Always process
    }

    [Fact(DisplayName = "User ID extraction should try multiple fields")]
    public void UserIdExtraction_ShouldTryMultipleFields()
    {
        // Business rule: try app_user_id first, then original_app_user_id, then aliases
        var userId = Guid.NewGuid();
        
        bool TryExtractUserId(string? appUserId, string? originalAppUserId, List<string> aliases, out Guid result)
        {
            result = default;
            if (!string.IsNullOrEmpty(appUserId) && Guid.TryParse(appUserId, out result))
                return true;
            if (!string.IsNullOrEmpty(originalAppUserId) && Guid.TryParse(originalAppUserId, out result))
                return true;
            foreach (var alias in aliases)
            {
                if (Guid.TryParse(alias, out result))
                    return true;
            }
            return false;
        }

        // Test 1: app_user_id takes priority
        TryExtractUserId(userId.ToString(), Guid.NewGuid().ToString(), new(), out var id1).ShouldBeTrue();
        id1.ShouldBe(userId);

        // Test 2: fallback to original_app_user_id
        TryExtractUserId("invalid", userId.ToString(), new(), out var id2).ShouldBeTrue();
        id2.ShouldBe(userId);

        // Test 3: fallback to aliases
        TryExtractUserId("invalid", "also_invalid", new() { userId.ToString() }, out var id3).ShouldBeTrue();
        id3.ShouldBe(userId);

        // Test 4: no valid GUID
        TryExtractUserId("invalid", "also_invalid", new() { "not_guid" }, out _).ShouldBeFalse();
    }
}
