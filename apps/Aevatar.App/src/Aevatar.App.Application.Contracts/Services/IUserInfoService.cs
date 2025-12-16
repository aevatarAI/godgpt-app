using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.UserInfo.Dtos;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// User Info service - manages user information collection during onboarding
/// </summary>
public interface IUserInfoService
{
    /// <summary>
    /// Update user information collection
    /// </summary>
    Task<UserInfoCollectionResponseDto> UpdateUserInfoCollectionAsync(Guid userId, UpdateUserInfoCollectionDto updateDto);
    
    /// <summary>
    /// Get user information collection
    /// </summary>
    Task<UserInfoCollectionDto> GetUserInfoCollectionAsync(Guid userId);
    
    /// <summary>
    /// Get user info display data
    /// </summary>
    Task<UserInfoDisplayDto> GetUserInfoDisplayAsync(Guid userId);
    
    /// <summary>
    /// Clear all user info collection data
    /// </summary>
    Task ClearAllAsync(Guid userId);
    
    /// <summary>
    /// Get user info options (seeking interests, source channels)
    /// </summary>
    Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync(Guid userId);
    
    /// <summary>
    /// Generate user info prompt for AI
    /// </summary>
    Task<Tuple<string, string>> GenerateUserInfoPromptAsync(Guid userId, DateTime? userLocalTime = null);
}

