using Aevatar.Payment.Agents.Protos;
using Shouldly;

namespace Aevatar.Payment.Agents.Tests;

/// <summary>
/// Unit tests for PaymentIndexGAgent
/// </summary>
public class PaymentIndexGAgentTests
{
    #region Platform Customer ID Tests

    [Fact(DisplayName = "Should set and get platform customer ID")]
    public async Task Should_Set_And_Get_Platform_Customer_Id()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();

        // Act
        await agent.SetPlatformCustomerIdAsync(PaymentPlatform.Stripe, "cus_123456");
        var customerId = await agent.GetPlatformCustomerIdAsync(PaymentPlatform.Stripe);

        // Assert
        customerId.ShouldBe("cus_123456");
    }

    [Fact(DisplayName = "Should return null for non-existent platform customer ID")]
    public async Task Should_Return_Null_For_NonExistent_Customer_Id()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();

        // Act
        var customerId = await agent.GetPlatformCustomerIdAsync(PaymentPlatform.AppStore);

        // Assert
        customerId.ShouldBeNull();
    }

    [Fact(DisplayName = "Should support multiple platform customer IDs")]
    public async Task Should_Support_Multiple_Platform_Customer_Ids()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();

        // Act
        await agent.SetPlatformCustomerIdAsync(PaymentPlatform.Stripe, "cus_stripe_123");
        await agent.SetPlatformCustomerIdAsync(PaymentPlatform.AppStore, "apple_user_456");
        await agent.SetPlatformCustomerIdAsync(PaymentPlatform.GooglePlay, "google_789");

        // Assert
        var stripeId = await agent.GetPlatformCustomerIdAsync(PaymentPlatform.Stripe);
        var appleId = await agent.GetPlatformCustomerIdAsync(PaymentPlatform.AppStore);
        var googleId = await agent.GetPlatformCustomerIdAsync(PaymentPlatform.GooglePlay);

        stripeId.ShouldBe("cus_stripe_123");
        appleId.ShouldBe("apple_user_456");
        googleId.ShouldBe("google_789");
    }

    #endregion

    #region Active Subscription Tests

    [Fact(DisplayName = "Should add active subscription")]
    public async Task Should_Add_Active_Subscription()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();
        var subscription = CreateTestSubscription("payment_1");

        // Act
        await agent.AddActiveSubscriptionAsync(subscription);
        var subscriptions = await agent.GetActiveSubscriptionsAsync();

        // Assert
        subscriptions.Subscriptions.ShouldNotBeEmpty();
        subscriptions.Subscriptions.Count.ShouldBe(1);
        subscriptions.Subscriptions[0].PaymentId.ShouldBe("payment_1");
    }

    [Fact(DisplayName = "Should get active subscriptions by business type")]
    public async Task Should_Get_Active_Subscriptions_By_Business_Type()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();
        await agent.AddActiveSubscriptionAsync(CreateTestSubscription("payment_1", "godgpt"));
        await agent.AddActiveSubscriptionAsync(CreateTestSubscription("payment_2", "course"));
        await agent.AddActiveSubscriptionAsync(CreateTestSubscription("payment_3", "godgpt"));

        // Act
        var godgptSubs = await agent.GetActiveSubscriptionsByBusinessAsync("godgpt");
        var courseSubs = await agent.GetActiveSubscriptionsByBusinessAsync("course");

        // Assert
        godgptSubs.Subscriptions.Count.ShouldBe(2);
        courseSubs.Subscriptions.Count.ShouldBe(1);
    }

    [Fact(DisplayName = "Should remove active subscription")]
    public async Task Should_Remove_Active_Subscription()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();
        await agent.AddActiveSubscriptionAsync(CreateTestSubscription("payment_1"));
        await agent.AddActiveSubscriptionAsync(CreateTestSubscription("payment_2"));

        // Act
        await agent.RemoveActiveSubscriptionAsync("payment_1");
        var subscriptions = await agent.GetActiveSubscriptionsAsync();

        // Assert
        subscriptions.Subscriptions.Count.ShouldBe(1);
        subscriptions.Subscriptions[0].PaymentId.ShouldBe("payment_2");
    }

    [Fact(DisplayName = "Should update subscription period end")]
    public async Task Should_Update_Subscription_Period_End()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();
        var initialEnd = DateTime.UtcNow.AddMonths(1);
        var subscription = CreateTestSubscription("payment_1", periodEnd: initialEnd);
        await agent.AddActiveSubscriptionAsync(subscription);

        // Act
        var newEnd = DateTime.UtcNow.AddMonths(2);
        await agent.UpdateSubscriptionPeriodEndAsync("payment_1", newEnd);
        var subscriptions = await agent.GetActiveSubscriptionsAsync();

        // Assert
        subscriptions.Subscriptions[0].PeriodEnd.ToDateTime().ShouldBeGreaterThan(initialEnd);
    }

    [Fact(DisplayName = "Should check if user has active subscription")]
    public async Task Should_Check_Has_Active_Subscription()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();

        // Act & Assert - No subscription
        var hasSubBefore = await agent.HasActiveSubscriptionAsync();
        hasSubBefore.ShouldBeFalse();

        // Add subscription
        await agent.AddActiveSubscriptionAsync(CreateTestSubscription("payment_1", "godgpt"));

        // Act & Assert - Has subscription
        var hasSubAfter = await agent.HasActiveSubscriptionAsync();
        hasSubAfter.ShouldBeTrue();

        // Act & Assert - Check by business type
        var hasGodgpt = await agent.HasActiveSubscriptionAsync("godgpt");
        var hasCourse = await agent.HasActiveSubscriptionAsync("course");
        hasGodgpt.ShouldBeTrue();
        hasCourse.ShouldBeFalse();
    }

    #endregion

    #region Statistics Tests

    [Fact(DisplayName = "Should track total payment count")]
    public async Task Should_Track_Total_Payment_Count()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();

        // Act
        var initialCount = await agent.GetTotalPaymentCountAsync();
        await agent.IncrementPaymentCountAsync();
        await agent.IncrementPaymentCountAsync();
        await agent.IncrementPaymentCountAsync();
        var finalCount = await agent.GetTotalPaymentCountAsync();

        // Assert
        initialCount.ShouldBe(0);
        finalCount.ShouldBe(3);
    }

    #endregion

    #region Clear Tests

    [Fact(DisplayName = "Should clear all data")]
    public async Task Should_Clear_All_Data()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentIndexGAgent>();
        await agent.SetPlatformCustomerIdAsync(PaymentPlatform.Stripe, "cus_123");
        await agent.AddActiveSubscriptionAsync(CreateTestSubscription("payment_1"));
        await agent.IncrementPaymentCountAsync();

        // Act
        await agent.ClearAllAsync();

        // Assert
        var customerId = await agent.GetPlatformCustomerIdAsync(PaymentPlatform.Stripe);
        var subscriptions = await agent.GetActiveSubscriptionsAsync();
        var count = await agent.GetTotalPaymentCountAsync();

        customerId.ShouldBeNull();
        subscriptions.Subscriptions.ShouldBeEmpty();
        count.ShouldBe(0);
    }

    #endregion

    #region Helper Methods

    private static ActiveSubscriptionProto CreateTestSubscription(
        string paymentId, 
        string businessType = "godgpt",
        DateTime? periodEnd = null)
    {
        return new ActiveSubscriptionProto
        {
            PaymentId = paymentId,
            BusinessType = businessType,
            BusinessId = "product_123",
            Platform = (int)PaymentPlatform.Stripe,
            ProductName = "Premium Plan",
            Amount = 999, // $9.99 in cents
            Currency = "USD",
            PeriodEnd = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
                (periodEnd ?? DateTime.UtcNow.AddMonths(1)).ToUniversalTime()),
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    #endregion
}
