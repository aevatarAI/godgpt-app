using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.GodGPT.Dtos;
using Aevatar.Payment.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;
using NewPaymentPlatform = Aevatar.Payment.Abstractions.PaymentPlatform;
using PaymentStripeOptions = Aevatar.Payment.Providers.StripeOptions;
using PaymentStripeProductConfig = Aevatar.Payment.Providers.StripeProductConfig;

namespace Aevatar.App.Services;

/// <summary>
/// Unit tests for InvitationService
/// Tests business logic for redeeming invite codes
/// 
/// Code Format Rules (from InvitationCodeHelper):
/// - FreeTrialCode: 11-12 characters, uppercase letters and numbers only (ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789)
/// - FriendInvitation: exactly 7 characters, Base62 (0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ)
/// </summary>
public class InvitationServiceTests
{
    private readonly IGAgentActorFactory _mockActorFactory;
    private readonly ILogger<InvitationService> _mockLogger;
    private readonly IPaymentService _mockPaymentService;
    private readonly IOptionsMonitor<CreditsOptions> _mockCreditsOptions;
    private readonly IOptionsMonitor<PaymentStripeOptions> _mockStripeOptions;
    private readonly InvitationService _invitationService;
    
    // Generate a valid FreeTrialCode format (11-12 uppercase chars/digits)
    private static string GenerateTestFreeTrialCode()
    {
        // Use InvitationCodeHelper to generate a real valid code
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return InvitationCodeHelper.GenerateOptimizedCode(InviteCodeType.FreeTrialReward, timestamp);
    }

    public InvitationServiceTests()
    {
        _mockActorFactory = Substitute.For<IGAgentActorFactory>();
        _mockLogger = Substitute.For<ILogger<InvitationService>>();
        _mockPaymentService = Substitute.For<IPaymentService>();
        _mockCreditsOptions = new TestOptionsMonitor<CreditsOptions>(new CreditsOptions
        {
            OperatorUserId = new List<string> { "test-operator-1" }
        });
        _mockStripeOptions = new TestOptionsMonitor<PaymentStripeOptions>(new PaymentStripeOptions
        {
            Products = new List<PaymentStripeProductConfig>
            {
                new Aevatar.Payment.Providers.StripeProductConfig
                {
                    PriceId = "price_test_monthly",
                    PlanType = 2,
                    Amount = 9.99m,
                    Currency = "USD",
                    IsUltimate = false
                }
            }
        });
        
        _invitationService = new InvitationService(
            _mockActorFactory,
            _mockLogger,
            _mockPaymentService,
            _mockCreditsOptions,
            _mockStripeOptions);
    }

    private class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static BatchInfoProto CreateBatchInfoProto()
    {
        return new BatchInfoProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Config = new BatchConfig
            {
                TrialDays = 7,
                ProductId = "price_test_monthly",
                PlanType = FactoryPlanType.Month,
                IsUltimate = false,
                Platform = FactoryPaymentPlatform.Stripe,
                StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
                EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), DateTimeKind.Utc)),
                Description = "Test batch"
            }
        };
    }

    private void SetupFreeTrialFactoryAgent(IFreeTrialCodeFactoryGAgent agent)
    {
        var actor = Substitute.For<IGAgentActor>();
        actor.GetAgent().Returns(agent);
        _mockActorFactory
            .CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(actor);
    }

    #region GetInvitationCodeTypeAsync Tests

    [Fact(DisplayName = "GetInvitationCodeTypeAsync should identify FriendInvitation code (7 chars)")]
    public async Task GetInvitationCodeTypeAsync_ShouldIdentifyFriendInvitationCode()
    {
        // Arrange - FriendInvitation is 7 Base62 characters
        var userId = Guid.NewGuid();
        var friendCode = "abc1234"; // Exactly 7 Base62 chars
        var request = new GetInvitationCodeTypeRequest { InviteCode = friendCode };

        // Act
        var result = await _invitationService.GetInvitationCodeTypeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.CodeType.ShouldBe(InvitationCodeType.FriendInvitation);
    }

    [Fact(DisplayName = "GetInvitationCodeTypeAsync should identify FreeTrialReward code (11-12 chars)")]
    public async Task GetInvitationCodeTypeAsync_ShouldIdentifyFreeTrialRewardCode()
    {
        // Arrange - FreeTrialCode is 11-12 uppercase chars/digits
        var userId = Guid.NewGuid();
        var trialCode = GenerateTestFreeTrialCode();
        var request = new GetInvitationCodeTypeRequest { InviteCode = trialCode };

        // Act
        var result = await _invitationService.GetInvitationCodeTypeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.CodeType.ShouldBe(InvitationCodeType.FreeTrialReward);
    }

    [Fact(DisplayName = "GetInvitationCodeTypeAsync should default to FriendInvitation for invalid format")]
    public async Task GetInvitationCodeTypeAsync_ShouldDefaultToFriendInvitationForInvalidFormat()
    {
        // Arrange - Invalid format (contains dash)
        var userId = Guid.NewGuid();
        var invalidCode = "TEST-1234";
        var request = new GetInvitationCodeTypeRequest { InviteCode = invalidCode };

        // Act
        var result = await _invitationService.GetInvitationCodeTypeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.CodeType.ShouldBe(InvitationCodeType.FriendInvitation); // Default fallback
    }

    #endregion

    #region RedeemInviteCodeAsync Tests - FreeTrialReward

    [Fact(DisplayName = "RedeemInviteCodeAsync should reject FreeTrialCode when IsWeb is false")]
    public async Task RedeemInviteCodeAsync_FreeTrialCode_ShouldRejectNonWeb()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var trialCode = GenerateTestFreeTrialCode();
        var request = new RedeemInviteCodeRequest
        {
            InviteCode = trialCode,
            IsWeb = false  // Non-web request should be rejected
        };

        // Act
        var result = await _invitationService.RedeemInviteCodeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeFalse();
        result.CodeType.ShouldBe(InvitationCodeType.FreeTrialReward);
        result.URL.ShouldBeNull();
        
        // Verify PaymentService was NOT called
        await _mockPaymentService.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<Guid>(),
            Arg.Any<NewPaymentPlatform>(),
            Arg.Any<SubscriptionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "RedeemInviteCodeAsync should return success when Stripe creates session")]
    public async Task RedeemInviteCodeAsync_FreeTrialCode_ShouldReturnSuccessWhenStripeSucceeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var trialCode = GenerateTestFreeTrialCode();
        var expectedUrl = "https://checkout.stripe.com/pay/cs_test_xxx";
        var request = new RedeemInviteCodeRequest
        {
            InviteCode = trialCode,
            IsWeb = true
        };

        var factoryAgent = Substitute.For<IFreeTrialCodeFactoryGAgent>();
        factoryAgent
            .ValidateCodeAvailableAsync(Arg.Any<ValidateCodeRequestProto>())
            .Returns(true);
        factoryAgent
            .GetBatchInfoAsync()
            .Returns(CreateBatchInfoProto());
        factoryAgent
            .MarkCodeAsUsedAsync(Arg.Any<MarkCodeUsedRequestProto>())
            .Returns(true);
        SetupFreeTrialFactoryAgent(factoryAgent);

        _mockPaymentService.CreateSubscriptionAsync(
            Arg.Is<Guid>(id => id == userId),
            Arg.Is<NewPaymentPlatform>(p => p == NewPaymentPlatform.Stripe),
            Arg.Is<SubscriptionRequest>(r => r.ProductId == "price_test_monthly" && r.TrialDays == 7),
            Arg.Any<CancellationToken>())
            .Returns(new SubscriptionResult
            {
                Success = true,
                SessionUrl = expectedUrl,
                SubscriptionId = "sub_123"
            });

        // Act
        var result = await _invitationService.RedeemInviteCodeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeTrue();
        result.CodeType.ShouldBe(InvitationCodeType.FreeTrialReward);
        result.URL.ShouldBe(expectedUrl);

        await factoryAgent.Received(1)
            .MarkCodeAsUsedAsync(Arg.Any<MarkCodeUsedRequestProto>());
    }

    [Fact(DisplayName = "RedeemInviteCodeAsync should return failure when Stripe fails")]
    public async Task RedeemInviteCodeAsync_FreeTrialCode_ShouldReturnFailureWhenStripeFails()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var trialCode = GenerateTestFreeTrialCode();
        var request = new RedeemInviteCodeRequest
        {
            InviteCode = trialCode,
            IsWeb = true
        };

        var factoryAgent = Substitute.For<IFreeTrialCodeFactoryGAgent>();
        factoryAgent
            .ValidateCodeAvailableAsync(Arg.Any<ValidateCodeRequestProto>())
            .Returns(true);
        factoryAgent
            .GetBatchInfoAsync()
            .Returns(CreateBatchInfoProto());
        SetupFreeTrialFactoryAgent(factoryAgent);

        _mockPaymentService.CreateSubscriptionAsync(
            Arg.Any<Guid>(),
            Arg.Any<NewPaymentPlatform>(),
            Arg.Any<SubscriptionRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(new SubscriptionResult
            {
                Success = false,
                ErrorMessage = "Invalid coupon code",
                SessionUrl = null
            });

        // Act
        var result = await _invitationService.RedeemInviteCodeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeFalse();
        result.URL.ShouldBeNull();

        await factoryAgent.DidNotReceive()
            .MarkCodeAsUsedAsync(Arg.Any<MarkCodeUsedRequestProto>());
    }

    [Fact(DisplayName = "RedeemInviteCodeAsync should handle Stripe exceptions gracefully")]
    public async Task RedeemInviteCodeAsync_FreeTrialCode_ShouldHandleExceptionsGracefully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var trialCode = GenerateTestFreeTrialCode();
        var request = new RedeemInviteCodeRequest
        {
            InviteCode = trialCode,
            IsWeb = true
        };

        var factoryAgent = Substitute.For<IFreeTrialCodeFactoryGAgent>();
        factoryAgent
            .ValidateCodeAvailableAsync(Arg.Any<ValidateCodeRequestProto>())
            .Returns(true);
        factoryAgent
            .GetBatchInfoAsync()
            .Returns(CreateBatchInfoProto());
        SetupFreeTrialFactoryAgent(factoryAgent);

        _mockPaymentService.CreateSubscriptionAsync(
            Arg.Any<Guid>(),
            Arg.Any<NewPaymentPlatform>(),
            Arg.Any<SubscriptionRequest>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Stripe API unavailable"));

        // Act
        var result = await _invitationService.RedeemInviteCodeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeFalse();
        result.CodeType.ShouldBe(InvitationCodeType.FreeTrialReward);

        await factoryAgent.DidNotReceive()
            .MarkCodeAsUsedAsync(Arg.Any<MarkCodeUsedRequestProto>());
    }

    [Fact(DisplayName = "RedeemInviteCodeAsync should fail when SessionUrl is empty")]
    public async Task RedeemInviteCodeAsync_FreeTrialCode_ShouldFailWhenSessionUrlEmpty()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var trialCode = GenerateTestFreeTrialCode();
        var request = new RedeemInviteCodeRequest
        {
            InviteCode = trialCode,
            IsWeb = true
        };

        var factoryAgent = Substitute.For<IFreeTrialCodeFactoryGAgent>();
        factoryAgent
            .ValidateCodeAvailableAsync(Arg.Any<ValidateCodeRequestProto>())
            .Returns(true);
        factoryAgent
            .GetBatchInfoAsync()
            .Returns(CreateBatchInfoProto());
        SetupFreeTrialFactoryAgent(factoryAgent);

        _mockPaymentService.CreateSubscriptionAsync(
            Arg.Any<Guid>(),
            Arg.Any<NewPaymentPlatform>(),
            Arg.Any<SubscriptionRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(new SubscriptionResult
            {
                Success = true,  // Success is true but no URL
                SessionUrl = string.Empty
            });

        // Act
        var result = await _invitationService.RedeemInviteCodeAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeFalse();
        result.URL.ShouldBeNull();

        await factoryAgent.DidNotReceive()
            .MarkCodeAsUsedAsync(Arg.Any<MarkCodeUsedRequestProto>());
    }

    #endregion
}
