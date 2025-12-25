using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.UserInfoCollection;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Aevatar.App.Services;

/// <summary>
/// Unit tests for UserInfoService
/// Tests business logic for user information collection
/// </summary>
public class UserInfoServiceTests
{
    private readonly IGAgentActorFactory _mockActorFactory;
    private readonly ILogger<UserInfoService> _mockLogger;
    private readonly UserInfoService _userInfoService;

    public UserInfoServiceTests()
    {
        _mockActorFactory = Substitute.For<IGAgentActorFactory>();
        _mockLogger = Substitute.For<ILogger<UserInfoService>>();
        
        _userInfoService = new UserInfoService(
            _mockActorFactory,
            _mockLogger);
    }

    [Fact(DisplayName = "UpdateUserInfoCollectionAsync should update user info successfully")]
    public async Task UpdateUserInfoCollectionAsync_ShouldUpdateUserInfoSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            UserId = userId,
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "USA",
                City = "New York"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 15,
                Month = 6,
                Year = 1990
            },
            SeekingInterests = new()
            {
                Aevatar.Application.Grains.UserInfo.Enums.SeekingInterestEnum.SelfDiscovery
            },
            SourceChannels = new()
            {
                Aevatar.Application.Grains.UserInfo.Enums.SourceChannelEnum.SocialMedia
            }
        };

        var actor = Substitute.For<IGAgentActor, IUserInfoCollectionGAgent>();
        var agent = (IUserInfoCollectionGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId.ToString())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new UserInfoCollectionResponseProto
        {
            Success = true,
            Message = "User info updated successfully"
        };
        
        agent.UpdateUserInfoCollectionAsync(Arg.Any<UpdateUserInfoCollectionRequestProto>())
            .Returns(protoResponse);

        // Act
        var result = await _userInfoService.UpdateUserInfoCollectionAsync(userId, updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("User info updated successfully");
    }

    [Fact(DisplayName = "GetUserInfoCollectionAsync should return user info collection")]
    public async Task GetUserInfoCollectionAsync_ShouldReturnUserInfoCollection()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var actor = Substitute.For<IGAgentActor, IUserInfoCollectionGAgent>();
        var agent = (IUserInfoCollectionGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId.ToString())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new UserInfoCollectionProto
        {
            UserId = userId.ToString(),
            IsInitialized = true,
            IsCompleted = true,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        protoResponse.NameInfo = new UserNameInfoProto
        {
            Gender = 1,
            FirstName = "John",
            LastName = "Doe"
        };
        
        agent.GetUserInfoCollectionAsync()
            .Returns(protoResponse);

        // Act
        var result = await _userInfoService.GetUserInfoCollectionAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        result.UserId.ShouldBe(userId);
        result.IsInitialized.ShouldBeTrue();
        result.NameInfo.ShouldNotBeNull();
        result.NameInfo.FirstName.ShouldBe("John");
    }

    [Fact(DisplayName = "GetUserInfoDisplayAsync should return user info display")]
    public async Task GetUserInfoDisplayAsync_ShouldReturnUserInfoDisplay()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var actor = Substitute.For<IGAgentActor, IUserInfoCollectionGAgent>();
        var agent = (IUserInfoCollectionGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId.ToString())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new UserInfoDisplayProto
        {
            FirstName = "John",
            LastName = "Doe",
            Gender = 1,
            Day = 15,
            Month = 6,
            Year = 1990,
            Country = "USA",
            City = "New York"
        };
        
        agent.GetUserInfoDisplayAsync()
            .Returns(protoResponse);

        // Act
        var result = await _userInfoService.GetUserInfoDisplayAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        result.FirstName.ShouldBe("John");
        result.LastName.ShouldBe("Doe");
        result.Gender.ShouldBe(1);
    }
}

