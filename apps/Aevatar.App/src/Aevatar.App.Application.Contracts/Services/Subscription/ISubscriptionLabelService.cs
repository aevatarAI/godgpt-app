using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription.Dtos;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service interface for subscription labels.
/// </summary>
public interface ISubscriptionLabelService
{
    /// <summary>
    /// Gets all subscription labels.
    /// </summary>
    Task<List<SubscriptionLabelDto>> GetAllLabelsAsync();

    /// <summary>
    /// Gets a label by ID.
    /// </summary>
    Task<SubscriptionLabelDto?> GetLabelAsync(string labelId);

    /// <summary>
    /// Creates a new subscription label.
    /// </summary>
    Task<SubscriptionLabelDto> CreateLabelAsync(CreateSubscriptionLabelDto dto);

    /// <summary>
    /// Updates an existing subscription label.
    /// </summary>
    Task<SubscriptionLabelDto> UpdateLabelAsync(string labelId, UpdateSubscriptionLabelDto dto);

    /// <summary>
    /// Deletes a subscription label.
    /// </summary>
    Task DeleteLabelAsync(string labelId);
}
