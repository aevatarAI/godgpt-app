using System;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Favourite;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Lumen;

/// <summary>
/// Unit tests for LumenFavouriteGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class LumenFavouriteGAgentTests
{
    private LumenFavouriteGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<LumenFavouriteGAgent>();
    }

    [Fact(DisplayName = "Should initialize with empty state")]
    public async Task ShouldInitializeWithEmptyState()
    {
        // Arrange & Act
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Assert
        agent.Id.ShouldNotBeNullOrEmpty();
        var state = agent.GetState();
        state.ShouldNotBeNull();
        state.Favourites.ShouldNotBeNull();
        state.Favourites.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Lumen favourite");
    }

    [Fact(DisplayName = "ToggleFavouriteAsync should add favourite when not exists")]
    public async Task ToggleFavouriteAsync_ShouldAddFavourite_WhenNotExists()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var request = new ToggleFavouriteRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            Date = new DateValue { Year = 2025, Month = 1, Day = 2 },
            IsFavourite = true
        };

        // Act
        var result = await agent.ToggleFavouriteAsync(request);

        // Assert
        result.Success.ShouldBeTrue();
        result.IsFavourite.ShouldBeTrue();
        var state = agent.GetState();
        state.Favourites.ContainsKey("pred-001").ShouldBeTrue();
    }

    [Fact(DisplayName = "ToggleFavouriteAsync should remove favourite when exists")]
    public async Task ToggleFavouriteAsync_ShouldRemoveFavourite_WhenExists()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var addRequest = new ToggleFavouriteRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            Date = new DateValue { Year = 2025, Month = 1, Day = 2 },
            IsFavourite = true
        };
        await agent.ToggleFavouriteAsync(addRequest);

        // Act - Remove
        var removeRequest = new ToggleFavouriteRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            Date = new DateValue { Year = 2025, Month = 1, Day = 2 },
            IsFavourite = false
        };
        var result = await agent.ToggleFavouriteAsync(removeRequest);

        // Assert
        result.Success.ShouldBeTrue();
        result.IsFavourite.ShouldBeFalse();
        var state = agent.GetState();
        state.Favourites.ContainsKey("pred-001").ShouldBeFalse();
    }

    [Fact(DisplayName = "IsFavouriteAsync should return true when prediction is favourited")]
    public async Task IsFavouriteAsync_ShouldReturnTrue_WhenPredictionIsFavourited()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var request = new ToggleFavouriteRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            Date = new DateValue { Year = 2025, Month = 1, Day = 2 },
            IsFavourite = true
        };
        await agent.ToggleFavouriteAsync(request);

        // Act
        var result = await agent.IsFavouriteAsync("pred-001");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "IsFavouriteAsync should return false when prediction is not favourited")]
    public async Task IsFavouriteAsync_ShouldReturnFalse_WhenPredictionIsNotFavourited()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.IsFavouriteAsync("non-existent");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetFavouritesAsync should return all favourites")]
    public async Task GetFavouritesAsync_ShouldReturnAllFavourites()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Add multiple favourites
        for (int i = 0; i < 3; i++)
        {
            var request = new ToggleFavouriteRequest
            {
                UserId = "user123",
                PredictionId = $"pred-{i:D3}",
                Date = new DateValue { Year = 2025, Month = 1, Day = i + 1 },
                IsFavourite = true
            };
            await agent.ToggleFavouriteAsync(request);
        }

        // Act
        var result = await agent.GetFavouritesAsync();

        // Assert
        result.Success.ShouldBeTrue();
        result.Favourites.Count.ShouldBe(3);
    }
}

