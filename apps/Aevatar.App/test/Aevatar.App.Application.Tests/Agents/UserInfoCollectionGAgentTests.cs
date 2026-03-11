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

    [Fact(DisplayName = "GenerateUserInfoPromptAsync should use provided user local time")]
    public async Task GenerateUserInfoPromptAsync_ShouldUseProvidedUserLocalTime()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new UpdateUserInfoCollectionRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            NameInfo = new UserNameInfoProto
            {
                Gender = 2,
                FirstName = "Ada",
                LastName = "Lovelace"
            },
            LocationInfo = new UserLocationInfoProto
            {
                Country = "UK",
                City = "London"
            },
            BirthDateInfo = new UserBirthDateInfoProto
            {
                Day = 1,
                Month = 12,
                Year = 1990
            }
        };
        request.SeekingInterests.Add(1);
        request.SourceChannels.Add(1);

        await agent.UpdateUserInfoCollectionAsync(request);

        var userLocalTime = new DateTime(2026, 3, 11, 9, 30, 15, DateTimeKind.Utc);

        // Act
        var response = await agent.GenerateUserInfoPromptAsync(new GenerateUserInfoPromptRequestProto
        {
            UserId = request.UserId,
            UserLocalTime = Timestamp.FromDateTime(userLocalTime)
        });

        // Assert
        response.Prompt.ShouldContain("User Message Time: 2026-03-11 09:30:15");
        response.Prompt.ShouldContain("User Name: Ada Lovelace");
        response.Prompt.ShouldContain("User Location: London, UK");
        response.Prompt.ShouldNotContain("Generate a personalized \"Today's Dos and Don'ts\"");
    }
}
