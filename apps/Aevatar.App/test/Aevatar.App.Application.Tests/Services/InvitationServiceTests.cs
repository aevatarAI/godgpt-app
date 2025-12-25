using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.GodGPT.Dtos;
using Aevatar.Payment.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;
using NewPaymentPlatform = Aevatar.Payment.Abstractions.PaymentPlatform;

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
        
        _invitationService = new InvitationService(
            _mockActorFactory,
            _mockLogger,
            _mockPaymentService);
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

        _mockPaymentService.CreateSubscriptionAsync(
            Arg.Is<Guid>(id => id == userId),
            Arg.Is<NewPaymentPlatform>(p => p == NewPaymentPlatform.Stripe),
            Arg.Is<SubscriptionRequest>(r => r.CouponCode == trialCode),
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
    }

    #endregion
}
