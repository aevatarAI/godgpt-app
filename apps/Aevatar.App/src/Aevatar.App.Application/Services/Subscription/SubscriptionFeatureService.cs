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
/// Application service for subscription features.
/// </summary>
[RemoteService(IsEnabled = false)]
public class SubscriptionFeatureService : AppAppService, ISubscriptionFeatureService
{
    private readonly IGAgentActorFactory _actorFactory;

    public SubscriptionFeatureService( IGAgentActorFactory actorFactory)
    {
        _actorFactory = actorFactory;
    }

    public async Task<List<SubscriptionFeatureDto>> GetAllFeaturesAsync(SubscriptionFeatureType? type = null)
    {
        var featureGAgent = await GetFeatureGAgentAsync();
        var featureList = type.HasValue
            ? await featureGAgent.GetFeaturesByTypeAsync(type.Value)
            : await featureGAgent.GetAllFeaturesAsync();

        return featureList.Features
            .OrderBy(f => f.DisplayOrder)
            .Select(MapToFeatureDto)
            .ToList();
    }

    public async Task<List<SubscriptionFeatureAdminDto>> GetAllFeaturesAdminAsync(
        SubscriptionFeatureType? type = null)
    {
        var featureGAgent = await GetFeatureGAgentAsync();
        var featureList = type.HasValue
            ? await featureGAgent.GetFeaturesByTypeAsync(type.Value)
            : await featureGAgent.GetAllFeaturesAsync();

        return featureList.Features
            .OrderBy(f => f.DisplayOrder)
            .Select(MapToFeatureAdminDto)
            .ToList();
    }

    public async Task<SubscriptionFeatureAdminDto?> GetFeatureAsync(string featureId)
    {
        var featureGAgent = await GetFeatureGAgentAsync();
        var feature = await featureGAgent.GetFeatureAsync(featureId);
        return feature != null ? MapToFeatureAdminDto(feature) : null;
    }

    public async Task<SubscriptionFeatureAdminDto> CreateFeatureAsync(CreateSubscriptionFeatureDto dto)
    {
        var featureGAgent = await GetFeatureGAgentAsync();
        await featureGAgent.CreateFeatureAsync(dto);

        var featureList = await featureGAgent.GetAllFeaturesAsync();
        var feature = featureList.Features.FirstOrDefault(f => f.NameKey == dto.NameKey);
        return MapToFeatureAdminDto(feature!);
    }

    public async Task<SubscriptionFeatureAdminDto> UpdateFeatureAsync(
        string featureId, UpdateSubscriptionFeatureDto dto)
    {
        var featureGAgent = await GetFeatureGAgentAsync();
        await featureGAgent.UpdateFeatureAsync(featureId, dto);

        var feature = await featureGAgent.GetFeatureAsync(featureId);
        return MapToFeatureAdminDto(feature!);
    }

    public async Task DeleteFeatureAsync(string featureId)
    {
        var featureGAgent = await GetFeatureGAgentAsync();
        await featureGAgent.DeleteFeatureAsync(featureId);
    }

    public async Task ReorderFeaturesAsync(List<SubscriptionFeatureOrderItemDto> orders)
    {
        var featureGAgent = await GetFeatureGAgentAsync();
        var orderListDto = new SubscriptionFeatureOrderListDto();
        orderListDto.Orders.AddRange(orders);
        await featureGAgent.ReorderFeaturesAsync(orderListDto);
    }

    #region GAgent
    
    private async Task<ISubscriptionFeatureGAgent> GetFeatureGAgentAsync()
    {
        var actor =
            await _actorFactory.CreateGAgentActorAsync<SubscriptionFeatureGAgent>(SubscriptionGAgentKeys.FeatureGAgentKey);
            
        return actor.As<ISubscriptionFeatureGAgent>();
    }

    #endregion

    #region Pure Mapping Methods

    private string GetFeatureTypeName(SubscriptionFeatureType type) => type switch
    {
        SubscriptionFeatureType.None => L["subscription.feature.type.none"],
        SubscriptionFeatureType.Core => L["subscription.feature.type.core"],
        SubscriptionFeatureType.Advanced => L["subscription.feature.type.advanced"],
        _ => type.ToString()
    };

    private SubscriptionFeatureDto MapToFeatureDto(SubscriptionFeature feature) => new()
    {
        Id = feature.Id,
        NameKey = feature.NameKey,
        Name = L[feature.NameKey],
        Description = (feature.DescriptionKey != null ? L[feature.DescriptionKey] : null) ?? string.Empty,
        Type = feature.Type,
        TypeName = GetFeatureTypeName(feature.Type),
        Usage = feature.Usage,
        DisplayOrder = feature.DisplayOrder
    };

    private SubscriptionFeatureAdminDto MapToFeatureAdminDto(SubscriptionFeature feature) => new()
    {
        Id = feature.Id,
        NameKey = feature.NameKey,
        DescriptionKey = feature.DescriptionKey,
        Name = L[feature.NameKey],
        Description = (feature.DescriptionKey != null ? L[feature.DescriptionKey] : null) ?? string.Empty,
        Type = feature.Type,
        TypeName = GetFeatureTypeName(feature.Type),
        Usage = feature.Usage,
        DisplayOrder = feature.DisplayOrder,
        CreatedAt = feature.CreatedAt.ToDateTime(),
        UpdatedAt = feature.UpdatedAt?.ToDateTime()
    };

    #endregion
}
