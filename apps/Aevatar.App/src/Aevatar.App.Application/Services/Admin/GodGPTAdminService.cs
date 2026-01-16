using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Common.Options;
using Aevatar.Dtos;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentStripeOptions = Aevatar.Payment.Providers.StripeOptions;
using PaymentStripeProductConfig = Aevatar.Payment.Providers.StripeProductConfig;
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
    private readonly IOptionsMonitor<CreditsOptions> _creditsOptions;
    private readonly IOptionsMonitor<PaymentStripeOptions> _stripeOptions;
    private readonly ILogger<GodGPTAdminService> _logger;

    public GodGPTAdminService(
        IGAgentActorFactory actorFactory,
        IOptionsMonitor<ManagerOptions> managerOptions,
        IOptionsMonitor<CreditsOptions> creditsOptions,
        IOptionsMonitor<PaymentStripeOptions> stripeOptions,
        ILogger<GodGPTAdminService> logger)
    {
        _actorFactory = actorFactory;
        _managerOptions = managerOptions;
        _creditsOptions = creditsOptions;
        _stripeOptions = stripeOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid currentUserId, GenerateFreeTrialCodeRequest input)
    {
        if (!IsOperatorAuthorized(currentUserId))
        {
            _logger.LogWarning("[GodGPTAdminService] Unauthorized attempt to generate codes by user {UserId}", currentUserId);
            return BuildGenerateCodesFailure("Unauthorized attempt to generate code");
        }

        if (input.Platform != PaymentPlatform.Stripe)
        {
            _logger.LogWarning("[GodGPTAdminService] Unsupported payment platform: {Platform}", input.Platform);
            return BuildGenerateCodesFailure($"Unsupported payment platform: {input.Platform}");
        }

        if (!TryGetStripeProductConfig(input.ProductId, out var productConfig, out var productError))
        {
            _logger.LogWarning("[GodGPTAdminService] Invalid product id for free trial code: {ProductId}", input.ProductId);
            return BuildGenerateCodesFailure(productError);
        }

        var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var agentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId);
        var factoryActor = await _actorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(
            agentId.ToString());
        var factoryGAgent = factoryActor.As<IFreeTrialCodeFactoryGAgent>();

        var batchConfig = new BatchConfig
        {
            TrialDays = input.TrialDays,
            ProductId = productConfig.PriceId,
            PlanType = MapPlanType((int)productConfig.PlanType),
            IsUltimate = productConfig.IsUltimate,
            Platform = (FactoryPaymentPlatform)(int)input.Platform,
            StartTime = Timestamp.FromDateTime(input.StartTime.ToUniversalTime()),
            EndTime = Timestamp.FromDateTime(input.EndTime.ToUniversalTime()),
            Description = input.Description ?? string.Empty
        };
        
        var request = new GenerateCodesRequestProto
        {
            BatchId = batchId,
            ProductId = input.ProductId,
            Platform = (FactoryPaymentPlatform)(int)input.Platform,
            TrialDays = input.TrialDays,
            StartTime = Timestamp.FromDateTime(input.StartTime.ToUniversalTime()),
            EndTime = Timestamp.FromDateTime(input.EndTime.ToUniversalTime()),
            Quantity = input.Quantity,
            OperatorUserId = currentUserId.ToString(),
            BatchConfig = batchConfig
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

    private bool IsOperatorAuthorized(Guid userId)
    {
        var operators = _creditsOptions.CurrentValue.OperatorUserId ?? new List<string>();
        var operatorId = userId.ToString();
        if (string.IsNullOrEmpty(operatorId))
        {
            return false;
        }
        return operators.Contains(operatorId);
    }

    private bool TryGetStripeProductConfig(string productId, out PaymentStripeProductConfig productConfig, out string errorMessage)
    {
        productConfig = null!;
        errorMessage = string.Empty;

        var products = _stripeOptions.CurrentValue.Products;
        if (products == null || products.Count == 0)
        {
            errorMessage = "Stripe products are not configured";
            return false;
        }

        var matched = products.FirstOrDefault(p => p.PriceId == productId);
        if (matched == null)
        {
            errorMessage = $"Invalid priceId: {productId}. Product not found in configuration.";
            return false;
        }

        productConfig = matched;
        return true;
    }

    private static FactoryPlanType MapPlanType(int planType)
    {
        return planType switch
        {
            1 => FactoryPlanType.Day,
            2 => FactoryPlanType.Month,
            3 => FactoryPlanType.Year,
            4 => FactoryPlanType.Week,
            _ => FactoryPlanType.None
        };
    }

    private static GenerateCodesResultDto BuildGenerateCodesFailure(string message)
    {
        return new GenerateCodesResultDto
        {
            Success = false,
            Message = message,
            Codes = new HashSet<string>(),
            GeneratedCount = 0,
            ErrorCode = FreeTrialCodeError.InternalError,
            BatchId = 0
        };
    }
}

