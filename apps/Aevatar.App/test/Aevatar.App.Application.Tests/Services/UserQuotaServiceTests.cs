using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.GodGPT.Dtos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Aevatar.App.Services;

/// <summary>
/// Unit tests for UserQuotaService
/// Tests business logic for user quota management
/// </summary>
public class UserQuotaServiceTests
{
    private readonly IGAgentActorFactory _mockActorFactory;
    private readonly ILogger<UserQuotaService> _mockLogger;
    private readonly UserQuotaService _userQuotaService;

    public UserQuotaServiceTests()
    {
        _mockActorFactory = Substitute.For<IGAgentActorFactory>();
        _mockLogger = Substitute.For<ILogger<UserQuotaService>>();
        
        _userQuotaService = new UserQuotaService(
            _mockActorFactory,
            _mockLogger);
    }

    [Fact(DisplayName = "SetShownCreditsToastAsync should set toast flag")]
    public async Task SetShownCreditsToastAsync_ShouldSetToastFlag()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var mockActor = Substitute.For<IGAgentActor>();
        
        _mockActorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId.ToString())
            .Returns(Task.FromResult(mockActor));

        // SetShownCreditsToastAsync returns void
        TestHelpers.SetupRpcMockVoid(mockActor, "SetShownCreditsToastAsync");

        // Act & Assert - should not throw
        await _userQuotaService.SetShownCreditsToastAsync(userId, true);
    }

    [Fact(DisplayName = "UpdateUserCreditsAsync should update credits successfully")]
    public async Task UpdateUserCreditsAsync_ShouldUpdateCreditsSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var input = new UpdateUserCreditsInput
        {
            UserId = userId,
            Credits = 100
        };

        var mockActor = Substitute.For<IGAgentActor>();
        
        _mockActorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId.ToString())
            .Returns(Task.FromResult(mockActor));
        
        var protoResponse = new UpdateCreditsResponseProto
        {
            Success = true,
            Message = "Credits updated successfully",
            Data = 500
        };
        
        TestHelpers.SetupRpcMock<IUserQuotaGAgent>(mockActor, "UpdateCreditsAsync", protoResponse);

        // Act
        var result = await _userQuotaService.UpdateUserCreditsAsync(operatorId, input);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldBe(500);
    }

    [Fact(DisplayName = "UpdateUserSubscriptionAsync should update subscription successfully")]
    public async Task UpdateUserSubscriptionAsync_ShouldUpdateSubscriptionSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var input = new UpdateUserSubscriptionsInput
        {
            UserId = userId,
            PlanType = PlanType.Month,
            IsUltimate = false
        };

        var mockActor = Substitute.For<IGAgentActor>();
        
        _mockActorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId.ToString())
            .Returns(Task.FromResult(mockActor));
        
        var protoResponse = new UpdateSubscriptionResponseProto
        {
            Success = true,
            Message = "Subscription updated successfully"
        };
        protoResponse.Data.Add(new SubscriptionInfoProto
        {
            IsActive = true,
            PlanType = (QuotaPlanType)2,
            Status = QuotaPaymentStatus.Completed,
            StartDate = Timestamp.FromDateTime(DateTime.UtcNow),
            EndDate = Timestamp.FromDateTime(DateTime.UtcNow.AddMonths(1))
        });
        
        TestHelpers.SetupRpcMock<IUserQuotaGAgent>(mockActor, "UpdateSubscriptionAsync", protoResponse);

        // Act
        var result = await _userQuotaService.UpdateUserSubscriptionAsync(operatorId, input);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(1);
        result.Data[0].IsActive.ShouldBeTrue();
        result.Data[0].PlanType.ShouldBe(PlanType.Month);
    }

    [Fact(DisplayName = "CanUploadImageAsync should return can upload result")]
    public async Task CanUploadImageAsync_ShouldReturnCanUploadResult()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var mockActor = Substitute.For<IGAgentActor>();
        
        _mockActorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId.ToString())
            .Returns(Task.FromResult(mockActor));
        
        var protoResponse = new CanUploadImageResponseProto
        {
            Success = true,
            CanUpload = true
        };
        
        TestHelpers.SetupRpcMock<IUserQuotaGAgent>(mockActor, "CanUploadImageAsync", protoResponse);

        // Act
        var result = await _userQuotaService.CanUploadImageAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Code.ShouldBe(0);
    }
}
