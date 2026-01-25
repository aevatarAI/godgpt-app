using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription.Dtos;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service interface for subscription features.
/// </summary>
public interface ISubscriptionFeatureService
{
    /// <summary>
    /// Gets all features for public display (user-facing).
    /// </summary>
    Task<List<SubscriptionFeatureDto>> GetAllFeaturesAsync(SubscriptionFeatureType? type = null);

    /// <summary>
    /// Gets all features for admin management.
    /// </summary>
    Task<List<SubscriptionFeatureAdminDto>> GetAllFeaturesAdminAsync(SubscriptionFeatureType? type = null);

    /// <summary>
    /// Gets a feature by ID.
    /// </summary>
    Task<SubscriptionFeatureAdminDto?> GetFeatureAsync(string featureId);

    /// <summary>
    /// Creates a new subscription feature.
    /// </summary>
    Task<SubscriptionFeatureAdminDto> CreateFeatureAsync(CreateSubscriptionFeatureDto dto);

    /// <summary>
    /// Updates an existing subscription feature.
    /// </summary>
    Task<SubscriptionFeatureAdminDto> UpdateFeatureAsync(string featureId, UpdateSubscriptionFeatureDto dto);

    /// <summary>
    /// Deletes a subscription feature.
    /// </summary>
    Task DeleteFeatureAsync(string featureId);

    /// <summary>
    /// Reorders subscription features.
    /// </summary>
    Task ReorderFeaturesAsync(List<SubscriptionFeatureOrderItemDto> orders);
}
