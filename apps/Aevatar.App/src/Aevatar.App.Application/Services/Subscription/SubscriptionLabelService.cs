using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription;
using Volo.Abp;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service for subscription labels.
/// </summary>
[RemoteService(IsEnabled = false)]
public class SubscriptionLabelService : AppAppService, ISubscriptionLabelService
{
    private readonly IGAgentActorFactory _actorFactory;

    public SubscriptionLabelService(
        IGAgentActorFactory actorFactory )
    {
        _actorFactory = actorFactory;
    }

    public async Task<List<SubscriptionLabelDto>> GetAllLabelsAsync()
    {
        var labelGAgent = await GetLabelGAgentAsync();
        var labelList = await labelGAgent.GetAllLabelsAsync();
        return labelList.Labels.Select(MapToLabelDto).ToList();
    }

    public async Task<SubscriptionLabelDto?> GetLabelAsync(string labelId)
    {
        var labelGAgent = await GetLabelGAgentAsync();
        var label = await labelGAgent.GetLabelAsync(labelId);
        return label != null ? MapToLabelDto(label) : null;
    }

    public async Task<SubscriptionLabelDto> CreateLabelAsync(CreateSubscriptionLabelDto dto)
    {
        var labelGAgent = await GetLabelGAgentAsync();
        var label = await labelGAgent.CreateLabelAsync(dto);
        return MapToLabelDto(label);
    }

    public async Task<SubscriptionLabelDto> UpdateLabelAsync(string labelId, UpdateSubscriptionLabelDto dto)
    {
        var labelGAgent = await GetLabelGAgentAsync();
        var label = await labelGAgent.UpdateLabelAsync(labelId, dto);
        return MapToLabelDto(label);
    }

    public async Task DeleteLabelAsync(string labelId)
    {
        var labelGAgent = await GetLabelGAgentAsync();
        await labelGAgent.DeleteLabelAsync(labelId);
    }

    #region GAgent 
    
    private async Task<ISubscriptionLabelGAgent> GetLabelGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<SubscriptionLabelGAgent>(SubscriptionGAgentKeys.LabelGAgentKey);
            
        return actor.As<ISubscriptionLabelGAgent>();
    }

    #endregion

    #region Pure Mapping Methods

    private SubscriptionLabelDto MapToLabelDto(SubscriptionLabel label) => new()
    {
        Id = label.Id,
        NameKey = label.NameKey,
        Name = L[$"{label.NameKey}"],
        CreatedAt = label.CreatedAt.ToDateTime()
    };

    #endregion
}
