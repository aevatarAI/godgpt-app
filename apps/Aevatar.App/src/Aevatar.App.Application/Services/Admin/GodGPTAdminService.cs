using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Common.Options;
using Aevatar.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace Aevatar.App.Application.Services.Admin;

/// <summary>
/// Service implementation for admin operations.
/// Handles free trial code generation and other administrative functions.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTAdminService : ApplicationService, IGodGPTAdminService
{
    private readonly IGAgentFactory _agentFactory;
    private readonly IOptionsMonitor<ManagerOptions> _managerOptions;
    private readonly ILogger<GodGPTAdminService> _logger;

    public GodGPTAdminService(
        IGAgentFactory agentFactory,
        IOptionsMonitor<ManagerOptions> managerOptions,
        ILogger<GodGPTAdminService> logger)
    {
        _agentFactory = agentFactory;
        _managerOptions = managerOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid currentUserId, GenerateFreeTrialCodeRequest input)
    {
        var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var factoryGAgent = _agentFactory.CreateGAgent<FreeTrialCodeFactoryGAgent>(
            CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId));
        
        return await factoryGAgent.GenerateCodesAsync(new GenerateCodesRequestDto
        {
            BatchId = batchId,
            ProductId = input.ProductId,
            Platform = input.Platform,
            TrialDays = input.TrialDays,
            StartTime = input.StartTime,
            EndTime = input.EndTime,
            Quantity = input.Quantity,
            OperatorUserId = currentUserId
        });
    }

    /// <inheritdoc />
    public async Task<BatchInfoDto> GetBatchInfoAsync(string batchId)
    {
        var factoryGAgent = _agentFactory.CreateGAgent<FreeTrialCodeFactoryGAgent>(
            CommonHelper.GetFreeTrialCodeFactoryGAgentId(long.Parse(batchId)));
        return await factoryGAgent.GetBatchInfoAsync();
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

