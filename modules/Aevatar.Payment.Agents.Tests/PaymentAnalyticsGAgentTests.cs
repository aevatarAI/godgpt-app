using Aevatar.Payment.Agents.Protos;
using Xunit;

namespace Aevatar.Payment.Agents.Tests;

/// <summary>
/// Unit tests for PaymentAnalyticsGAgent
/// </summary>
public class PaymentAnalyticsGAgentTests
{
    [Fact]
    public async Task HandlePaymentCompleted_ShouldReportPurchase_WhenNotAlreadyReported()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentAnalyticsGAgent>();
        var mockService = new MockPaymentAnalyticsService();
        agent.AnalyticsService = mockService;

        var evt = new PaymentCompletedEvent
        {
            TransactionId = "txn_123",
            IsRenewal = false,
            Context = new PaymentEventContext
            {
                PaymentId = "payment_123",
                UserId = "user_456",
                Platform = (int)PaymentPlatform.Stripe
            }
        };

        // Act
        await agent.HandlePaymentCompleted(evt);

        // Assert
        Assert.True(mockService.PurchaseReported);
        Assert.Equal("txn_123", mockService.LastTransactionId);
        Assert.Equal("Stripe", mockService.LastPlatform);
    }

    [Fact]
    public async Task HandlePaymentCompleted_ShouldSkip_WhenAlreadyReported()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentAnalyticsGAgent>();
        var mockService = new MockPaymentAnalyticsService();
        agent.AnalyticsService = mockService;

        // First call
        var evt = new PaymentCompletedEvent
        {
            TransactionId = "txn_123",
            Context = new PaymentEventContext
            {
                UserId = "user_456",
                Platform = (int)PaymentPlatform.Stripe
            }
        };
        await agent.HandlePaymentCompleted(evt);

        // Reset mock
        mockService.Reset();

        // Act - second call with same transaction
        await agent.HandlePaymentCompleted(evt);

        // Assert - should skip
        Assert.False(mockService.PurchaseReported);
    }

    [Fact]
    public async Task HandleRefundCompleted_ShouldReportRefund()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentAnalyticsGAgent>();
        var mockService = new MockPaymentAnalyticsService();
        agent.AnalyticsService = mockService;

        var evt = new RefundCompletedEvent
        {
            OriginalTransactionId = "txn_original",
            RefundAmount = 1000,
            Reason = "Customer request",
            Context = new PaymentEventContext
            {
                UserId = "user_456",
                Platform = (int)PaymentPlatform.AppStore
            }
        };

        // Act
        await agent.HandleRefundCompleted(evt);

        // Assert
        Assert.True(mockService.RefundReported);
        Assert.Equal("txn_original", mockService.LastTransactionId);
        Assert.Equal(1000, mockService.LastRefundAmount);
    }

    [Fact]
    public async Task HandlePaymentCompleted_ShouldNotBlock_WhenServiceNotAvailable()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentAnalyticsGAgent>();
        // Don't inject service - AnalyticsService is null

        var evt = new PaymentCompletedEvent
        {
            TransactionId = "txn_123",
            Context = new PaymentEventContext
            {
                UserId = "user_456",
                Platform = (int)PaymentPlatform.Stripe
            }
        };

        // Act - should not throw
        await agent.HandlePaymentCompleted(evt);

        // Assert - agent state should still be updated
        var description = await agent.GetDescriptionAsync();
        Assert.Contains("txn_123", description);
    }

    [Fact]
    public async Task HandlePaymentCompleted_ShouldRecordFailure_WhenServiceFails()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentAnalyticsGAgent>();
        var mockService = new MockPaymentAnalyticsService { ShouldFail = true };
        agent.AnalyticsService = mockService;

        var evt = new PaymentCompletedEvent
        {
            TransactionId = "txn_fail",
            Context = new PaymentEventContext
            {
                UserId = "user_456",
                Platform = (int)PaymentPlatform.Stripe
            }
        };

        // Act
        await agent.HandlePaymentCompleted(evt);

        // Assert - state should record failure
        var description = await agent.GetDescriptionAsync();
        Assert.Contains("Purchase: False", description); // Not reported
    }
}

/// <summary>
/// Mock implementation of IPaymentAnalyticsService for testing
/// </summary>
public class MockPaymentAnalyticsService : IPaymentAnalyticsService
{
    public bool PurchaseReported { get; private set; }
    public bool RefundReported { get; private set; }
    public string? LastTransactionId { get; private set; }
    public string? LastPlatform { get; private set; }
    public decimal LastRefundAmount { get; private set; }
    public bool ShouldFail { get; set; }

    public Task<bool> ReportPurchaseAsync(
        string platform,
        string transactionId,
        string userId,
        decimal amount,
        string currency,
        bool isRenewal = false,
        CancellationToken ct = default)
    {
        if (ShouldFail) return Task.FromResult(false);

        PurchaseReported = true;
        LastTransactionId = transactionId;
        LastPlatform = platform;
        return Task.FromResult(true);
    }

    public Task<bool> ReportRefundAsync(
        string platform,
        string transactionId,
        string userId,
        decimal refundAmount,
        string currency,
        string reason,
        CancellationToken ct = default)
    {
        if (ShouldFail) return Task.FromResult(false);

        RefundReported = true;
        LastTransactionId = transactionId;
        LastRefundAmount = refundAmount;
        return Task.FromResult(true);
    }

    public void Reset()
    {
        PurchaseReported = false;
        RefundReported = false;
        LastTransactionId = null;
        LastPlatform = null;
        LastRefundAmount = 0;
    }
}

