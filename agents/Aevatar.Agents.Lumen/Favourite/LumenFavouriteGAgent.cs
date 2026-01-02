using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.Favourite;

/// <summary>
/// Interface for Lumen Favourite GAgent - manages user's favourite predictions
/// </summary>
public interface ILumenFavouriteGAgent : IGAgent
{
    Task<ToggleFavouriteResult> ToggleFavouriteAsync(ToggleFavouriteRequest request);
    
    Task<GetFavouritesResult> GetFavouritesAsync();
    
    Task<bool> IsFavouriteAsync(string predictionId);
}

/// <summary>
/// Lumen Favourite GAgent - manages user's favourite predictions
/// 
/// New Framework: Inherits from GAgentBase, NOT Grain
/// Uses Protobuf State + Event Sourcing pattern
/// </summary>
public class LumenFavouriteGAgent : GAgentBase<LumenFavouriteState>, ILumenFavouriteGAgent
{
    private const int MaxFavourites = 100; // Maximum 100 favourites per user

    /// <summary>
    /// Required: Parameterless constructor for activation
    /// </summary>
    public LumenFavouriteGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Lumen favourite management - {State.Favourites.Count} favourites");
    }

    // ============================================================================
    // Event Handlers (new framework style)
    // ============================================================================

    [EventHandler]
    public void HandlePredictionFavouritedEvent(PredictionFavouritedEvent evt)
    {
        TransitionState(State, evt);
    }

    [EventHandler]
    public void HandlePredictionUnfavouritedEvent(PredictionUnfavouritedEvent evt)
    {
        TransitionState(State, evt);
    }

    // ============================================================================
    // State Transition (Pure Functional)
    // ============================================================================

    protected override void TransitionState(LumenFavouriteState state, IMessage evt)
    {
        switch (evt)
        {
            case PredictionFavouritedEvent favouritedEvent:
                state.UserId = favouritedEvent.UserId;
                state.Favourites[favouritedEvent.PredictionId] = favouritedEvent.FavouriteDetail;
                state.LastUpdatedAt = favouritedEvent.FavouritedAt;
                break;
                
            case PredictionUnfavouritedEvent unfavouritedEvent:
                state.UserId = unfavouritedEvent.UserId;
                state.Favourites.Remove(unfavouritedEvent.PredictionId);
                state.LastUpdatedAt = unfavouritedEvent.UnfavouritedAt;
                break;
        }
    }

    // ============================================================================
    // Public Methods (RPC Interface Implementation)
    // ============================================================================

    public async Task<ToggleFavouriteResult> ToggleFavouriteAsync(ToggleFavouriteRequest request)
    {
        try
        {
            Logger.LogDebug(
                "[LumenFavouriteGAgent][ToggleFavouriteAsync] Start - UserId: {UserId}, PredictionId: {PredictionId}, IsFavourite: {IsFavourite}",
                request.UserId, request.PredictionId, request.IsFavourite);

            // Validate request
            var validationResult = ValidateToggleRequest(request);
            if (!validationResult.IsValid)
            {
                Logger.LogWarning(
                    "[LumenFavouriteGAgent][ToggleFavouriteAsync] Validation failed: {Message}",
                    validationResult.Message);
                return new ToggleFavouriteResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }

            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            var isCurrentlyFavourite = State.Favourites.ContainsKey(request.PredictionId);

            // Check if action is needed
            if (request.IsFavourite == isCurrentlyFavourite)
            {
                Logger.LogInformation(
                    "[LumenFavouriteGAgent][ToggleFavouriteAsync] No change needed - PredictionId: {PredictionId}, IsFavourite: {IsFavourite}",
                    request.PredictionId, request.IsFavourite);
                return new ToggleFavouriteResult
                {
                    Success = true,
                    Message = string.Empty,
                    IsFavourite = isCurrentlyFavourite,
                    UpdatedAt = State.LastUpdatedAt
                };
            }

            if (request.IsFavourite)
            {
                // Add to favourites
                // Check max limit
                if (State.Favourites.Count >= MaxFavourites)
                {
                    Logger.LogWarning(
                        "[LumenFavouriteGAgent][ToggleFavouriteAsync] Max favourites limit reached: {UserId}",
                        request.UserId);
                    return new ToggleFavouriteResult
                    {
                        Success = false,
                        Message = $"Maximum favourites limit ({MaxFavourites}) reached. Please remove some favourites first."
                    };
                }

                var favouriteDetail = new FavouriteDetail
                {
                    Date = request.Date,
                    PredictionId = request.PredictionId,
                    FavouritedAt = now
                };

                RaiseEvent(new PredictionFavouritedEvent
                {
                    UserId = request.UserId,
                    Date = request.Date,
                    PredictionId = request.PredictionId,
                    FavouriteDetail = favouriteDetail,
                    FavouritedAt = now
                });
            }
            else
            {
                // Remove from favourites
                RaiseEvent(new PredictionUnfavouritedEvent
                {
                    UserId = request.UserId,
                    PredictionId = request.PredictionId,
                    UnfavouritedAt = now
                });
            }

            await ConfirmEventsAsync();

            Logger.LogInformation(
                "[LumenFavouriteGAgent][ToggleFavouriteAsync] Success - UserId: {UserId}, PredictionId: {PredictionId}, IsFavourite: {IsFavourite}",
                request.UserId, request.PredictionId, request.IsFavourite);

            return new ToggleFavouriteResult
            {
                Success = true,
                Message = string.Empty,
                IsFavourite = request.IsFavourite,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "[LumenFavouriteGAgent][ToggleFavouriteAsync] Error toggling favourite: {UserId}",
                request.UserId);
            return new ToggleFavouriteResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<GetFavouritesResult> GetFavouritesAsync()
    {
        try
        {
            Logger.LogDebug(
                "[LumenFavouriteGAgent][GetFavouritesAsync] Getting favourites for user: {UserId}",
                State.UserId);

            var result = new GetFavouritesResult
            {
                Success = true,
                Message = string.Empty
            };

            // Sort by date descending
            var sortedFavourites = State.Favourites.Values
                .OrderByDescending(f => f.Date != null 
                    ? new DateTime(f.Date.Year, f.Date.Month, f.Date.Day)
                    : DateTime.MinValue)
                .ToList();

            foreach (var f in sortedFavourites)
            {
                result.Favourites.Add(new FavouriteItemDto
                {
                    Date = f.Date,
                    PredictionId = f.PredictionId,
                    FavouritedAt = f.FavouritedAt
                });
            }

            Logger.LogInformation(
                "[LumenFavouriteGAgent][GetFavouritesAsync] Found {Count} favourites",
                result.Favourites.Count);

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenFavouriteGAgent][GetFavouritesAsync] Error getting favourites");
            return Task.FromResult(new GetFavouritesResult
            {
                Success = false,
                Message = "Internal error occurred"
            });
        }
    }

    public Task<bool> IsFavouriteAsync(string predictionId)
    {
        try
        {
            Logger.LogDebug(
                "[LumenFavouriteGAgent][IsFavouriteAsync] Checking if prediction is favourite: {PredictionId}", 
                predictionId);
            return Task.FromResult(State.Favourites.ContainsKey(predictionId));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenFavouriteGAgent][IsFavouriteAsync] Error checking favourite");
            return Task.FromResult(false);
        }
    }

    // ============================================================================
    // Private Helper Methods
    // ============================================================================

    /// <summary>
    /// Validate toggle favourite request
    /// </summary>
    private (bool IsValid, string Message) ValidateToggleRequest(ToggleFavouriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return (false, "UserId is required");
        }

        if (string.IsNullOrWhiteSpace(request.PredictionId))
        {
            return (false, "PredictionId is required");
        }

        if (request.Date == null)
        {
            return (false, "Date is required");
        }

        // Validate date is not in the future
        var requestDate = new DateOnly(request.Date.Year, request.Date.Month, request.Date.Day);
        if (requestDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return (false, "Cannot favourite future predictions");
        }

        return (true, string.Empty);
    }
}

