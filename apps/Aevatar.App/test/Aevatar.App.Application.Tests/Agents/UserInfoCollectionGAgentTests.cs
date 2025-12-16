using System;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.UserInfoCollection;
using Aevatar.Application.Grains.UserInfo;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents;

/// <summary>
/// Unit tests for UserInfoCollectionGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class UserInfoCollectionGAgentTests
{
    private UserInfoCollectionGAgent CreateAgent()
    {
        var agent = TestHelpers.CreateAgent<UserInfoCollectionGAgent>();
        return agent;
    }

    [Fact(DisplayName = "UpdateUserInfoCollectionAsync should update user info successfully")]
    public async Task UpdateUserInfoCollectionAsync_ShouldUpdateUserInfoSuccessfully()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new UpdateUserInfoCollectionRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            NameInfo = new UserNameInfoProto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            },
            LocationInfo = new UserLocationInfoProto
            {
                Country = "USA",
                City = "New York"
            },
            BirthDateInfo = new UserBirthDateInfoProto
            {
                Day = 15,
                Month = 6,
                Year = 1990
            }
        };
        request.SeekingInterests.Add(1);
        request.SourceChannels.Add(1);

        // Act
        var result = await agent.UpdateUserInfoCollectionAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        
        var state = agent.GetState();
        state.FirstName.ShouldBe("John");
        state.LastName.ShouldBe("Doe");
        state.Country.ShouldBe("USA");
        state.City.ShouldBe("New York");
    }

    [Fact(DisplayName = "GetUserInfoCollectionAsync should return null for uninitialized user")]
    public async Task GetUserInfoCollectionAsync_ShouldReturnNullForUninitializedUser()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var result = await agent.GetUserInfoCollectionAsync();

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "GetUserInfoDisplayAsync should return null for uninitialized user")]
    public async Task GetUserInfoDisplayAsync_ShouldReturnNullForUninitializedUser()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var result = await agent.GetUserInfoDisplayAsync();

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "ClearAllAsync should clear all user info")]
    public async Task ClearAllAsync_ShouldClearAllUserInfo()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new UpdateUserInfoCollectionRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            NameInfo = new UserNameInfoProto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            }
        };
        request.SeekingInterests.Add(1);
        request.SourceChannels.Add(1);
        
        await agent.UpdateUserInfoCollectionAsync(request);

        // Act
        await agent.ClearAllAsync();

        // Assert
        var state = agent.GetState();
        state.FirstName.ShouldBeNullOrEmpty();
        state.LastName.ShouldBeNullOrEmpty();
    }
}

