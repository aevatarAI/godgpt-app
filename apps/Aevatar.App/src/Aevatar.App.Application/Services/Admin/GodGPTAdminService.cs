using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Common.Options;
using Aevatar.Dtos;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.App.Application.Contracts.Services.Admin;

namespace Aevatar.App.Application.Services.Admin;

/// <summary>
/// Service implementation for admin operations.
/// Handles free trial code generation and other administrative functions.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTAdminService : ApplicationService, IGodGPTAdminService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IOptionsMonitor<ManagerOptions> _managerOptions;
    private readonly ILogger<GodGPTAdminService> _logger;

    public GodGPTAdminService(
        IGAgentActorFactory actorFactory,
        IOptionsMonitor<ManagerOptions> managerOptions,
        ILogger<GodGPTAdminService> logger)
    {
        _actorFactory = actorFactory;
        _managerOptions = managerOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid currentUserId, GenerateFreeTrialCodeRequest input)
    {
        var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId);
        var factoryActor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(
            agentId.ToString());
        var factoryGAgent = factoryActor.As<IFreeTrialCodeFactoryGAgent>();
        
        var request = new GenerateCodesRequestProto
        {
            BatchId = batchId,
            ProductId = input.ProductId,
            Platform = (FactoryPaymentPlatform)(int)input.Platform,
            TrialDays = input.TrialDays,
            StartTime = Timestamp.FromDateTime(input.StartTime.ToUniversalTime()),
            EndTime = Timestamp.FromDateTime(input.EndTime.ToUniversalTime()),
            Quantity = input.Quantity,
            OperatorUserId = currentUserId.ToString()
        };
        
        var result = await factoryGAgent.GenerateCodesAsync(request);
        
        // Convert Protobuf result to DTO
        return new GenerateCodesResultDto
        {
            Success = result.Success,
            Message = result.Message,
            GeneratedCount = result.GeneratedCount,
            Codes = new HashSet<string>(result.Codes),
            BatchId = batchId
        };
    }

    /// <inheritdoc />
    public async Task<BatchInfoDto> GetBatchInfoAsync(string batchId)
    {
        var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(long.Parse(batchId));
        var factoryActor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(
            agentId.ToString());
        var factoryGAgent = factoryActor.As<IFreeTrialCodeFactoryGAgent>();
        var batchInfo = await factoryGAgent.GetBatchInfoAsync();
        
        // Convert Protobuf to DTO
        // Note: BatchInfoDto structure matches BatchInfoProto fields
        return new BatchInfoDto
        {
            BatchId = batchInfo.BatchId,
            TotalGenerated = batchInfo.TotalGenerated,
            UsedCount = batchInfo.UsedCount,
            CreationTime = batchInfo.CreationTime.ToDateTime().ToUniversalTime(),
            LastGenerationTime = batchInfo.LastGenerationTime?.ToDateTime().ToUniversalTime() ?? DateTime.MinValue.ToUniversalTime(),
            Status = (FreeTrialCodeFactoryStatus)batchInfo.Status,
            GeneratedCodes = batchInfo.GeneratedCodes.ToList(),
            UsedCodes = batchInfo.UsedCodes.ToList()
        };
    }

    /// <inheritdoc />
    public Task<bool> CheckIsManagerAsync(Guid? currentUserId)
    {
        if (currentUserId == Guid.Empty || currentUserId == null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_managerOptions.CurrentValue.ManagerIds.Contains(currentUserId.ToString()));
    }
}

