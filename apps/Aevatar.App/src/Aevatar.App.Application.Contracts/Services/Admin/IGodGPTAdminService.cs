using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Dtos;

namespace Aevatar.App.Application.Contracts.Services.Admin;

/// <summary>
/// Service interface for admin operations.
/// Handles free trial code generation and other administrative functions.
/// </summary>
public interface IGodGPTAdminService
{
    /// <summary>
    /// Generates free trial codes.
    /// </summary>
    /// <param name="currentUserId">The current user ID (operator)</param>
    /// <param name="input">The request parameters for code generation</param>
    /// <returns>Result containing generated codes</returns>
    Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid currentUserId, GenerateFreeTrialCodeRequest input);

    /// <summary>
    /// Gets batch information for generated codes.
    /// </summary>
    /// <param name="batchId">The batch ID</param>
    /// <returns>Batch information</returns>
    Task<BatchInfoDto> GetBatchInfoAsync(string batchId);

    /// <summary>
    /// Checks if the user is a manager.
    /// </summary>
    /// <param name="currentUserId">The user ID to check</param>
    /// <returns>True if the user is a manager</returns>
    Task<bool> CheckIsManagerAsync(Guid? currentUserId);
}

