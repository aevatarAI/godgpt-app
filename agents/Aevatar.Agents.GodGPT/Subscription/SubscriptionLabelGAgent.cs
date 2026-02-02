using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for managing subscription labels with event sourcing.
/// </summary>
public class SubscriptionLabelGAgent : 
    GAgentBase<SubscriptionLabelState>,
    ISubscriptionLabelGAgent
{
    private readonly ILogger<SubscriptionLabelGAgent> _logger;

    public SubscriptionLabelGAgent(ILogger<SubscriptionLabelGAgent> logger)
    {
        _logger = logger;
    }

    #region CRUD Operations

    public async Task<SubscriptionLabel> CreateLabelAsync(CreateSubscriptionLabelDto dto)
    {
        var labelId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("Creating subscription label: {LabelId}, NameKey: {NameKey}", 
            labelId, dto.NameKey);

        RaiseEvent(new SubscriptionLabelCreatedEvent
        {
            LabelId = labelId,
            NameKey = dto.NameKey
        });
        
        await ConfirmEventsAsync();
        
        return State.Labels[labelId];
    }

    public async Task<SubscriptionLabel> UpdateLabelAsync(string labelId, UpdateSubscriptionLabelDto dto)
    {
        if (!State.Labels.ContainsKey(labelId))
            throw new KeyNotFoundException($"Label not found: {labelId}");
        
        _logger.LogInformation("Updating subscription label: {LabelId}", labelId);

        RaiseEvent(new SubscriptionLabelUpdatedEvent
        {
            LabelId = labelId,
            NameKey = dto.NameKey
        });
        
        await ConfirmEventsAsync();
        
        return State.Labels[labelId];
    }

    public async Task DeleteLabelAsync(string labelId)
    {
        if (!State.Labels.ContainsKey(labelId))
            throw new KeyNotFoundException($"Label not found: {labelId}");
        
        _logger.LogInformation("Deleting subscription label: {LabelId}", labelId);

        RaiseEvent(new SubscriptionLabelDeletedEvent { LabelId = labelId });
        
        await ConfirmEventsAsync();
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<SubscriptionLabel?> GetLabelAsync(string labelId)
    {
        if (State.Labels.TryGetValue(labelId, out var label))
            return Task.FromResult<SubscriptionLabel?>(label);
        return Task.FromResult<SubscriptionLabel?>(null);
    }

    public Task<SubscriptionLabelList> GetAllLabelsAsync()
    {
        var subscriptionLabelList = new SubscriptionLabelList();
        subscriptionLabelList.Labels.AddRange(State.Labels.Values);
        return Task.FromResult(subscriptionLabelList);
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages subscription labels.");
    }

    #endregion

    #region State Transition

    protected override void TransitionState(SubscriptionLabelState state, IMessage evt)
    {
        switch (evt)
        {
            case SubscriptionLabelCreatedEvent created:
                state.Labels[created.LabelId] = new SubscriptionLabel
                {
                    Id = created.LabelId,
                    NameKey = created.NameKey,
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                break;
                
            case SubscriptionLabelUpdatedEvent updated:
                if (state.Labels.TryGetValue(updated.LabelId, out var label))
                {
                    label.NameKey = updated.NameKey;
                    label.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                }
                break;
                
            case SubscriptionLabelDeletedEvent deleted:
                state.Labels.Remove(deleted.LabelId);
                break;
        }
    }

    #endregion
}
