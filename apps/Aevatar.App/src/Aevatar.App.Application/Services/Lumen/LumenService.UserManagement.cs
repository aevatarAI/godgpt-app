using System;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Services.Lumen;

public partial class LumenService
{
    #region User Management

    /// <inheritdoc />
    public async Task<UpdateUserProfileResult> UpdateUserProfileAsync(
        UpdateUserProfileRequest request, 
        string userPreferredLanguage = "en")
    {
        try
        {
            _logger.LogDebug("[LumenService][UpdateUserProfileAsync] Updating profile: {UserId}", request.UserId);

            var agent = await GetUserProfileAgentAsync(request.UserId);
            
            // Agent interface takes only the request, no language parameter
            var result = await agent.UpdateUserProfileAsync(request);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][UpdateUserProfileAsync] Error: {UserId}", request.UserId);
            return new UpdateUserProfileResult
            {
                Success = false,
                Message = $"Error updating user profile: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetUserProfileResult> GetUserProfileAsync(string userId, string userLanguage = "en")
    {
        try
        {
            _logger.LogDebug("[LumenService][GetUserProfileAsync] Getting profile: {UserId}", userId);

            var agent = await GetUserProfileAgentAsync(userId);
            var result = await agent.GetUserProfileAsync(userId, userLanguage);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetUserProfileAsync] Error: {UserId}", userId);
            return new GetUserProfileResult
            {
                Success = false,
                Message = $"Error getting user profile: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<ClearUserResult> ClearUserAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][ClearUserAsync] Clearing user: {UserId}", userId);

            var agent = await GetUserProfileAgentAsync(userId);
            var result = await agent.ClearUserAsync();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][ClearUserAsync] Error: {UserId}", userId);
            return new ClearUserResult
            {
                Success = false,
                Message = $"Error clearing user: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetRemainingUpdatesResult> GetRemainingUpdatesAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetRemainingUpdatesAsync] Getting remaining updates: {UserId}", userId);

            var agent = await GetUserProfileAgentAsync(userId);
            var result = await agent.GetRemainingUpdatesAsync();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetRemainingUpdatesAsync] Error: {UserId}", userId);
            return new GetRemainingUpdatesResult
            {
                Success = false
            };
        }
    }

    /// <inheritdoc />
    public async Task<UpdateIconResult> UpdateUserIconAsync(string userId, string? iconUrl)
    {
        try
        {
            _logger.LogDebug("[LumenService][UpdateUserIconAsync] Updating icon: {UserId}", userId);

            var agent = await GetUserProfileAgentAsync(userId);
            var result = await agent.UpdateIconAsync(iconUrl);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][UpdateUserIconAsync] Error: {UserId}", userId);
            return new UpdateIconResult
            {
                Success = false,
                Message = $"Error updating icon: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<UpdateIconResult> UploadUserIconAsync(string userId, Stream fileStream, string fileName, long fileSize)
    {
        try
        {
            _logger.LogDebug("[LumenService][UploadUserIconAsync] Start - UserId: {UserId}, FileName: {FileName}", userId, fileName);

            // Validate file size
            if (fileSize > _blobStoringOptions.MaxSizeBytes)
            {
                return new UpdateIconResult
                {
                    Success = false,
                    Message = $"File size exceeds maximum allowed size ({_blobStoringOptions.MaxSizeBytes / 1024 / 1024}MB)"
                };
            }

            // Get current icon URL before uploading new one (for deletion)
            var currentProfileResult = await GetUserProfileAsync(userId, "en");
            var oldIconUrl = currentProfileResult?.UserProfile?.Icon;

            // Upload to blob storage
            var fileExtension = Path.GetExtension(fileName);
            var blobFileName = $"lumen_icon_{userId}_{Guid.NewGuid()}{fileExtension}";

            await _blobContainer.SaveAsync(blobFileName, fileStream, true);

            _logger.LogDebug("[LumenService][UploadUserIconAsync] File uploaded to blob: {FileName}", blobFileName);

            // Update user profile icon via agent
            var result = await UpdateUserIconAsync(userId, blobFileName);

            // Delete old icon from blob storage if update was successful and old icon exists
            if (result.Success && !string.IsNullOrWhiteSpace(oldIconUrl))
            {
                try
                {
                    await _blobContainer.DeleteAsync(oldIconUrl);
                    _logger.LogInformation("[LumenService][UploadUserIconAsync] Deleted old icon from blob: {OldIcon}", oldIconUrl);
                }
                catch (Exception ex)
                {
                    // Log but don't fail the request if old icon deletion fails
                    _logger.LogWarning(ex, "[LumenService][UploadUserIconAsync] Failed to delete old icon: {OldIcon}", oldIconUrl);
                }
            }

            _logger.LogInformation("[LumenService][UploadUserIconAsync] Completed - UserId: {UserId}, Success: {Success}", userId, result.Success);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][UploadUserIconAsync] Error uploading icon: {UserId}", userId);
            return new UpdateIconResult
            {
                Success = false,
                Message = "Failed to upload icon"
            };
        }
    }

    /// <inheritdoc />
    public async Task<SetLanguageResult> SetLanguageAsync(string userId, string newLanguage)
    {
        try
        {
            _logger.LogDebug("[LumenService][SetLanguageAsync] Setting language: {UserId} -> {Language}", userId, newLanguage);

            var agent = await GetUserProfileAgentAsync(userId);
            var result = await agent.SetLanguageAsync(newLanguage);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][SetLanguageAsync] Error: {UserId}", userId);
            return new SetLanguageResult
            {
                Success = false,
                Message = $"Error setting language: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetLanguageInfoResult> GetLanguageInfoAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetLanguageInfoAsync] Getting language info: {UserId}", userId);

            var agent = await GetUserProfileAgentAsync(userId);
            var result = await agent.GetLanguageInfoAsync();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetLanguageInfoAsync] Error: {UserId}", userId);
            return new GetLanguageInfoResult
            {
                Success = false,
                Message = $"Error getting language info: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<UpdateTimeZoneResult> UpdateTimeZoneAsync(string userId, string timeZoneId)
    {
        try
        {
            _logger.LogDebug("[LumenService][UpdateTimeZoneAsync] Updating timezone: {UserId} -> {TimeZone}", userId, timeZoneId);

            var agent = await GetUserProfileAgentAsync(userId);
            var request = new UpdateTimeZoneRequest { TimeZoneId = timeZoneId };
            var result = await agent.UpdateTimeZoneAsync(request);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][UpdateTimeZoneAsync] Error: {UserId}", userId);
            return new UpdateTimeZoneResult
            {
                Success = false,
                Message = $"Error updating timezone: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<DateOnly> GetUserLocalDateAsync(string userId)
    {
        try
        {
            var agent = await GetUserProfileAgentAsync(userId);
            var profile = await agent.GetUserProfileAsync(userId, "en");
            
            if (profile.Success && profile.UserProfile != null)
            {
                return GetUserLocalDate(profile.UserProfile.CurrentTimeZone);
            }
            
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetUserLocalDateAsync] Error: {UserId}", userId);
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }

    #endregion
}
