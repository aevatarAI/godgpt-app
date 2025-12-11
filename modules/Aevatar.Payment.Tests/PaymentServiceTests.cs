using Aevatar.Agents.Abstractions;
using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;
using AgentModels = Aevatar.Payment.Agents;

namespace Aevatar.Payment.Tests;

/// <summary>
/// Unit tests for PaymentService - focuses on routing and orchestration logic
/// </summary>
public class PaymentServiceTests
{
    private readonly IPaymentProvider _stripeProvider;
    private readonly IPaymentProvider _appleProvider;
    private readonly IGAgentFactory _agentFactory;
    private readonly ILogger<PaymentService> _logger;
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _stripeProvider = Substitute.For<IPaymentProvider>();
        _stripeProvider.Platform.Returns(PaymentPlatform.Stripe);

        _appleProvider = Substitute.For<IPaymentProvider>();
        _appleProvider.Platform.Returns(PaymentPlatform.AppStore);

        _agentFactory = Substitute.For<IGAgentFactory>();
        _logger = Substitute.For<ILogger<PaymentService>>();

        _service = new PaymentService(
            new[] { _stripeProvider, _appleProvider },
            _agentFactory,
            _logger);
    }

    [Fact(DisplayName = "Should route to correct provider by platform")]
    public async Task ShouldRouteToCorrectProvider()
    {
        // Arrange
        _stripeProvider.GetProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto> { new() { ProductId = "stripe_price" } });
        _appleProvider.GetProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto> { new() { ProductId = "apple_product" } });

        // Act
        var stripeProducts = await _service.GetProductsAsync(PaymentPlatform.Stripe);
        var appleProducts = await _service.GetProductsAsync(PaymentPlatform.AppStore);

        // Assert
        stripeProducts[0].ProductId.ShouldBe("stripe_price");
        appleProducts[0].ProductId.ShouldBe("apple_product");
    }

    [Fact(DisplayName = "Should throw for unsupported platform")]
    public async Task ShouldThrowForUnsupportedPlatform()
    {
        await Should.ThrowAsync<NotSupportedException>(
            () => _service.GetProductsAsync(PaymentPlatform.GooglePlay));
    }

    [Fact(DisplayName = "Should record payment to agents on successful subscription")]
    public async Task ShouldRecordPaymentOnSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _stripeProvider.CreateSubscriptionAsync(Arg.Any<SubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionResult
            {
                Success = true,
                SubscriptionId = "sub_123",
                CustomerId = "cus_456"
            });

        var mockIndexAgent = Substitute.For<AgentModels.IPaymentIndexGAgent>();
        var mockRecordAgent = Substitute.For<AgentModels.IPaymentRecordGAgent>();
        
        _agentFactory.CreateGAgent<AgentModels.IPaymentIndexGAgent>(userId)
            .Returns(mockIndexAgent);
        _agentFactory.CreateGAgent<AgentModels.IPaymentRecordGAgent>(Arg.Any<Guid>())
            .Returns(mockRecordAgent);

        // Act
        var result = await _service.CreateSubscriptionAsync(
            userId, PaymentPlatform.Stripe, new SubscriptionRequest { ProductId = "price_123" });

        // Assert
        result.Success.ShouldBeTrue();
        await mockRecordAgent.Received(1).InitializeAsync(Arg.Any<AgentModels.CreatePaymentRequest>());
        await mockIndexAgent.Received(1).AddActiveSubscriptionAsync(Arg.Any<AgentModels.ActiveSubscription>());
    }

    [Fact(DisplayName = "Should NOT record payment on failed subscription")]
    public async Task ShouldNotRecordPaymentOnFailure()
    {
        // Arrange
        _stripeProvider.CreateSubscriptionAsync(Arg.Any<SubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionResult { Success = false, ErrorMessage = "Card declined" });

        // Act
        var result = await _service.CreateSubscriptionAsync(
            Guid.NewGuid(), PaymentPlatform.Stripe, new SubscriptionRequest());

        // Assert
        result.Success.ShouldBeFalse();
        _agentFactory.DidNotReceive().CreateGAgent<AgentModels.IPaymentIndexGAgent>(Arg.Any<Guid>());
    }

    [Fact(DisplayName = "Should aggregate active subscriptions from index agent")]
    public async Task ShouldAggregateSubscriptionsFromIndexAgent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var mockIndexAgent = Substitute.For<AgentModels.IPaymentIndexGAgent>();
        mockIndexAgent.GetActiveSubscriptionsAsync().Returns(new List<AgentModels.ActiveSubscription>
        {
            new() { PaymentId = "p1", ProductName = "Pro", Platform = AgentModels.PaymentPlatform.Stripe, Amount = 999 },
            new() { PaymentId = "p2", ProductName = "Basic", Platform = AgentModels.PaymentPlatform.AppStore, Amount = 499 }
        });
        _agentFactory.CreateGAgent<AgentModels.IPaymentIndexGAgent>(userId).Returns(mockIndexAgent);

        // Act
        var status = await _service.GetUserSubscriptionStatusAsync(userId);

        // Assert
        status.HasActiveSubscription.ShouldBeTrue();
        status.ActiveSubscriptions.Count.ShouldBe(2);
        status.CurrentPlan.ShouldBe("Pro"); // First subscription
    }
}
