using System;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen.Dtos;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Services.Lumen;

public partial class LumenService
{
    #region Feedback

    /// <inheritdoc />
    public async Task<SubmitFeedbackResult> SubmitFeedbackAsync(SubmitFeedbackRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenService][SubmitFeedbackAsync] Submitting feedback: {UserId}, {PredictionId}", 
                request.UserId, request.PredictionId);

            var feedbackAgent = await GetFeedbackAgentAsync(request.PredictionId);
            var result = await feedbackAgent.SubmitOrUpdateFeedbackAsync(request);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][SubmitFeedbackAsync] Error: {UserId}, {PredictionId}", 
                request.UserId, request.PredictionId);
            return new SubmitFeedbackResult
            {
                Success = false,
                Message = $"Error submitting feedback: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<UpdateMethodRatingResult> UpdateMethodRatingAsync(UpdateMethodRatingRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenService][UpdateMethodRatingAsync] Updating method rating: {UserId}, {PredictionId}", 
                request.UserId, request.PredictionId);

            var feedbackAgent = await GetFeedbackAgentAsync(request.PredictionId);
            var result = await feedbackAgent.UpdateMethodRatingAsync(request);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][UpdateMethodRatingAsync] Error: {UserId}, {PredictionId}", 
                request.UserId, request.PredictionId);
            return new UpdateMethodRatingResult
            {
                Success = false,
                Message = $"Error updating method rating: {ex.Message}"
            };
        }
    }

    #endregion

    #region Favourites

    /// <inheritdoc />
    public async Task<ToggleFavouriteResult> ToggleFavouriteAsync(ToggleFavouriteRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenService][ToggleFavouriteAsync] Toggling favourite: {UserId}, {PredictionId}", 
                request.UserId, request.PredictionId);

            var favouriteAgent = await GetFavouriteAgentAsync(request.UserId);
            var result = await favouriteAgent.ToggleFavouriteAsync(request);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][ToggleFavouriteAsync] Error: {UserId}, {PredictionId}", 
                request.UserId, request.PredictionId);
            return new ToggleFavouriteResult
            {
                Success = false,
                Message = $"Error toggling favourite: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetFavouritesResult> GetFavouritesAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetFavouritesAsync] Getting favourites: {UserId}", userId);

            var favouriteAgent = await GetFavouriteAgentAsync(userId);
            var result = await favouriteAgent.GetFavouritesAsync();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetFavouritesAsync] Error: {UserId}", userId);
            return new GetFavouritesResult
            {
                Success = false,
                Message = $"Error getting favourites: {ex.Message}"
            };
        }
    }

    #endregion

    #region Google Auth

    /// <inheritdoc />
    public async Task<GoogleAuthResultDto> GoogleAuthVerifyCodeAsync(string userId, GoogleAuthVerifyCodeInput input)
    {
        try
        {
            _logger.LogDebug("[LumenService][GoogleAuthVerifyCodeAsync] Verifying Google code: {UserId}", userId);

            // TODO: Implement Google OAuth verification
            _logger.LogWarning("[LumenService][GoogleAuthVerifyCodeAsync] Google OAuth not yet implemented");
            
            return new GoogleAuthResultDto
            {
                Success = false,
                Message = "Google OAuth not yet implemented"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GoogleAuthVerifyCodeAsync] Error: {UserId}", userId);
            return new GoogleAuthResultDto
            {
                Success = false,
                Message = $"Error verifying Google code: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> GoogleAuthUnbindAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][GoogleAuthUnbindAsync] Unbinding Google account: {UserId}", userId);

            // TODO: Implement Google unbind
            _logger.LogWarning("[LumenService][GoogleAuthUnbindAsync] Google unbind not yet implemented");
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GoogleAuthUnbindAsync] Error: {UserId}", userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<GoogleBindStatusDto> GoogleAuthBindStatusAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][GoogleAuthBindStatusAsync] Checking Google bind status: {UserId}", userId);

            // TODO: Implement Google bind status check
            _logger.LogWarning("[LumenService][GoogleAuthBindStatusAsync] Google bind status not yet implemented");
            
            return new GoogleBindStatusDto
            {
                IsBound = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GoogleAuthBindStatusAsync] Error: {UserId}", userId);
            return new GoogleBindStatusDto
            {
                IsBound = false
            };
        }
    }

    #endregion
}
