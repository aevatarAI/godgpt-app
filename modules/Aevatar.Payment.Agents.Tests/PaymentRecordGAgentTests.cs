using Aevatar.Payment.Agents.Protos;
using Shouldly;

namespace Aevatar.Payment.Agents.Tests;

/// <summary>
/// Unit tests for PaymentRecordGAgent
/// </summary>
public class PaymentRecordGAgentTests
{
    #region Initialization Tests

    [Fact(DisplayName = "Should not be initialized by default")]
    public async Task Should_Not_Be_Initialized_By_Default()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentRecordGAgent>();

        // Act
        var isInitialized = await agent.IsInitializedAsync();

        // Assert
        isInitialized.ShouldBeFalse();
    }

    [Fact(DisplayName = "Should initialize payment record")]
    public async Task Should_Initialize_Payment_Record()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentRecordGAgent>();
        var request = CreateTestRequest();

        // Act
        await agent.InitializeAsync(request);
        var isInitialized = await agent.IsInitializedAsync();
        var record = await agent.GetPaymentRecordAsync();

        // Assert
        isInitialized.ShouldBeTrue();
        record.UserId.ShouldBe("user_123");
        record.Platform.ShouldBe(PaymentPlatform.Stripe);
        record.BusinessType.ShouldBe("godgpt");
        record.ProductName.ShouldBe("Premium Plan");
        record.Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact(DisplayName = "Should not reinitialize already initialized agent")]
    public async Task Should_Not_Reinitialize_Already_Initialized_Agent()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentRecordGAgent>();
        var request1 = CreateTestRequest("user_123");
        var request2 = CreateTestRequest("user_456");

        // Act
        await agent.InitializeAsync(request1);
        await agent.InitializeAsync(request2); // Should be ignored
        var record = await agent.GetPaymentRecordAsync();

        // Assert
        record.UserId.ShouldBe("user_123"); // Should remain the first user
    }

    [Fact(DisplayName = "Should preserve business metadata")]
    public async Task Should_Preserve_Business_Metadata()
    {
        // Arrange
        var agent = TestHelpers.CreateAgent<PaymentRecordGAgent>();
        var request = CreateTestRequest();
        request.BusinessMetadata["promo_code"] = "SUMMER2024";
        request.BusinessMetadata["referral_id"] = "ref_abc";

        // Act
        await agent.InitializeAsync(request);
        var record = await agent.GetPaymentRecordAsync();

        // Assert
        record.BusinessMetadata.ShouldContainKeyAndValue("promo_code", "SUMMER2024");
        record.BusinessMetadata.ShouldContainKeyAndValue("referral_id", "ref_abc");
    }

    #endregion

    #region Status Update Tests

    [Fact(DisplayName = "Should update payment status")]
    public async Task Should_Update_Payment_Status()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();

        // Act
        await agent.UpdateStatusAsync(PaymentStatus.Processing);
        var status = await agent.GetStatusAsync();

        // Assert
        status.ShouldBe(PaymentStatus.Processing);
    }

    [Fact(DisplayName = "Should complete payment")]
    public async Task Should_Complete_Payment()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();

        // Act
        await agent.CompleteAsync();
        var status = await agent.GetStatusAsync();
        var record = await agent.GetPaymentRecordAsync();

        // Assert
        status.ShouldBe(PaymentStatus.Completed);
        record.CompletedAt.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Should cancel payment")]
    public async Task Should_Cancel_Payment()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();

        // Act
        await agent.CancelAsync("User requested cancellation");
        var status = await agent.GetStatusAsync();

        // Assert
        status.ShouldBe(PaymentStatus.Cancelled);
    }

    [Fact(DisplayName = "Should update period")]
    public async Task Should_Update_Period()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var newStart = DateTime.UtcNow;
        var newEnd = DateTime.UtcNow.AddMonths(1);

        // Act
        await agent.UpdatePeriodAsync(newStart, newEnd);
        var record = await agent.GetPaymentRecordAsync();

        // Assert
        record.PeriodStart.ShouldNotBeNull();
        record.PeriodEnd.ShouldNotBeNull();
    }

    #endregion

    #region Transaction Tests

    [Fact(DisplayName = "Should add transaction")]
    public async Task Should_Add_Transaction()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var transaction = CreateTestTransaction(TransactionType.Initial);

        // Act
        var transactionId = await agent.AddTransactionAsync(transaction);
        var transactions = await agent.GetTransactionsAsync();

        // Assert
        transactionId.ShouldNotBeNullOrEmpty();
        transactions.Count.ShouldBe(1);
        transactions[0].TransactionType.ShouldBe(TransactionType.Initial);
    }

    [Fact(DisplayName = "Should update transaction status")]
    public async Task Should_Update_Transaction_Status()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var transaction = CreateTestTransaction(TransactionType.Initial);
        var transactionId = await agent.AddTransactionAsync(transaction);

        // Act
        await agent.UpdateTransactionStatusAsync(transactionId, PaymentStatus.Completed);
        var updatedTx = await agent.GetTransactionAsync(transactionId);

        // Assert
        updatedTx.ShouldNotBeNull();
        updatedTx.Status.ShouldBe(PaymentStatus.Completed);
    }

    [Fact(DisplayName = "Should get specific transaction by ID")]
    public async Task Should_Get_Specific_Transaction_By_Id()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var tx1 = CreateTestTransaction(TransactionType.Initial);
        var tx2 = CreateTestTransaction(TransactionType.Renewal);
        var id1 = await agent.AddTransactionAsync(tx1);
        var id2 = await agent.AddTransactionAsync(tx2);

        // Act
        var foundTx = await agent.GetTransactionAsync(id2);

        // Assert
        foundTx.ShouldNotBeNull();
        foundTx.TransactionType.ShouldBe(TransactionType.Renewal);
    }

    #endregion

    #region Renewal Tests

    [Fact(DisplayName = "Should process renewal")]
    public async Task Should_Process_Renewal()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var renewal = new RenewalInfo
        {
            ExternalTransactionId = "ext_renewal_123",
            InvoiceId = "inv_456",
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddMonths(1),
            Amount = 999,
            Currency = "USD"
        };

        // Act
        await agent.ProcessRenewalAsync(renewal);
        var transactions = await agent.GetTransactionsAsync();
        var record = await agent.GetPaymentRecordAsync();

        // Assert
        transactions.Count.ShouldBe(1);
        transactions[0].TransactionType.ShouldBe(TransactionType.Renewal);
        transactions[0].Status.ShouldBe(PaymentStatus.Completed);
        record.PeriodEnd.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Should process renewal with promotions")]
    public async Task Should_Process_Renewal_With_Promotions()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var renewal = new RenewalInfo
        {
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddMonths(1),
            Amount = 799,
            Currency = "USD",
            Promotions = new List<Promotion>
            {
                new Promotion
                {
                    PromotionId = "promo_123",
                    PromotionType = "coupon",
                    Code = "DISCOUNT20",
                    PercentOff = 20
                }
            }
        };

        // Act
        await agent.ProcessRenewalAsync(renewal);
        var transactions = await agent.GetTransactionsAsync();

        // Assert
        transactions[0].Promotions.Count.ShouldBe(1);
        transactions[0].Promotions[0].Code.ShouldBe("DISCOUNT20");
    }

    [Fact(DisplayName = "Should handle trial renewal")]
    public async Task Should_Handle_Trial_Renewal()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var renewal = new RenewalInfo
        {
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddDays(7),
            Amount = 0,
            Currency = "USD",
            IsTrial = true,
            TrialCode = "TRIAL7DAYS"
        };

        // Act
        await agent.ProcessRenewalAsync(renewal);
        var transactions = await agent.GetTransactionsAsync();

        // Assert
        transactions[0].IsTrial.ShouldBeTrue();
        transactions[0].TrialCode.ShouldBe("TRIAL7DAYS");
    }

    #endregion

    #region Refund Tests

    [Fact(DisplayName = "Should process refund")]
    public async Task Should_Process_Refund()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        var initialTx = CreateTestTransaction(TransactionType.Initial);
        initialTx.Status = PaymentStatus.Completed;
        await agent.AddTransactionAsync(initialTx);
        await agent.CompleteAsync();

        var refund = new RefundInfo
        {
            RefundAmount = 999,
            Reason = "Customer requested refund"
        };

        // Act
        await agent.ProcessRefundAsync(refund);
        var status = await agent.GetStatusAsync();

        // Assert
        status.ShouldBe(PaymentStatus.Refunded);
    }

    [Fact(DisplayName = "Should process partial refund for specific transaction")]
    public async Task Should_Process_Partial_Refund()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync();
        
        // Add two transactions - both completed
        var tx1 = CreateTestTransaction(TransactionType.Initial);
        tx1.Status = PaymentStatus.Completed;
        var txId1 = await agent.AddTransactionAsync(tx1);
        
        var tx2 = CreateTestTransaction(TransactionType.Renewal);
        tx2.Status = PaymentStatus.Completed;
        await agent.AddTransactionAsync(tx2);

        // Act - refund only the first transaction
        await agent.ProcessPartialRefundAsync(txId1, 500, "Partial refund");
        var status = await agent.GetStatusAsync();

        // Assert - should be PartialRefunded because tx2 is still not refunded
        status.ShouldBe(PaymentStatus.PartialRefunded);
    }

    #endregion

    #region Query Tests

    [Fact(DisplayName = "Should get user ID")]
    public async Task Should_Get_User_Id()
    {
        // Arrange
        var agent = await CreateInitializedAgentAsync("user_test_123");

        // Act
        var userId = await agent.GetUserIdAsync();

        // Assert
        userId.ShouldBe("user_test_123");
    }

    #endregion

    #region Helper Methods

    private static CreatePaymentRequestProto CreateTestRequest(string userId = "user_123")
    {
        var request = new CreatePaymentRequestProto
        {
            UserId = userId,
            Platform = (int)PaymentPlatform.Stripe,
            SubscriptionId = "sub_123456",
            CustomerId = "cus_123456",
            ProductId = "price_premium",
            ProductName = "Premium Plan",
            PaymentMode = (int)PaymentMode.Subscription,
            BillingCycle = (int)BillingCycle.Monthly,
            BusinessType = "godgpt",
            BusinessId = "plan_premium",
            Amount = 999,
            Currency = "USD",
            Environment = "Production"
        };
        return request;
    }

    private static Transaction CreateTestTransaction(TransactionType type)
    {
        return new Transaction
        {
            ExternalTransactionId = $"ext_tx_{Guid.NewGuid():N}",
            TransactionType = type,
            Status = PaymentStatus.Pending,
            Amount = 999,
            Currency = "USD",
            CreatedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>(),
            Promotions = new List<Promotion>()
        };
    }

    private static async Task<PaymentRecordGAgent> CreateInitializedAgentAsync(string userId = "user_123")
    {
        var agent = TestHelpers.CreateAgent<PaymentRecordGAgent>();
        await agent.InitializeAsync(CreateTestRequest(userId));
        return agent;
    }

    #endregion
}
