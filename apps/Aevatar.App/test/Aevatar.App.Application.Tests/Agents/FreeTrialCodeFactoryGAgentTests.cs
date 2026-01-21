using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents;

/// <summary>
/// Unit tests for FreeTrialCodeFactoryGAgent
/// Uses TestHelpers pattern from Payment module - no ABP framework dependency
/// </summary>
public class FreeTrialCodeFactoryGAgentTests
{
    private FreeTrialCodeFactoryGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<FreeTrialCodeFactoryGAgent>();
    }

    private static BatchConfig CreateBatchConfig()
    {
        return new BatchConfig
        {
            TrialDays = 30,
            ProductId = "price_test_monthly",
            PlanType = FactoryPlanType.Month,
            IsUltimate = false,
            Platform = FactoryPaymentPlatform.Stripe,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Description = "Test batch"
        };
    }

    [Fact(DisplayName = "FreeTrialCodeFactoryGAgent should initialize with correct state")]
    public async Task FreeTrialCodeFactoryGAgent_ShouldInitializeWithCorrectState()
    {
        // Arrange & Act
        var agent = CreateAgent();

        // Assert
        agent.ShouldNotBeNull();
        var state = agent.GetState();
        state.ShouldNotBeNull();
    }

    [Fact(DisplayName = "GenerateCodesAsync should generate codes successfully")]
    public async Task GenerateCodesAsync_ShouldGenerateCodesSuccessfully()
    {
        // Arrange
        var agent = CreateAgent();

        var request = new GenerateCodesRequestProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProductId = "price_test_monthly",
            Platform = FactoryPaymentPlatform.Stripe,
            TrialDays = 30,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Quantity = 5,
            OperatorUserId = "test-operator-1",
            Description = "Test batch",
            BatchConfig = CreateBatchConfig()
        };

        // Act
        var result = await agent.GenerateCodesAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.GeneratedCount.ShouldBe(5);
        result.Codes.ShouldNotBeNull();
        result.Codes.Count.ShouldBe(5);
        
        var state = agent.GetState();
        state.HasBatchId.ShouldBeTrue();
        // Status becomes Completed after generation finishes
        state.Status.ShouldBe(FactoryStatus.Completed);
        state.TotalCodesGenerated.ShouldBe(5);
    }

    [Fact(DisplayName = "GenerateCodesAsync should reject missing batch config")]
    public async Task GenerateCodesAsync_ShouldRejectMissingBatchConfig()
    {
        // Arrange
        var agent = CreateAgent();

        var request = new GenerateCodesRequestProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProductId = "price_test_monthly",
            Platform = FactoryPaymentPlatform.Stripe,
            TrialDays = 30,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Quantity = 5,
            OperatorUserId = "test-operator-1",
            Description = "Test batch"
        };

        // Act
        var result = await agent.GenerateCodesAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Factory not initialized");
        result.ErrorCode.ShouldBe((int)FreeTrialCodeError.InternalError);
    }

    [Fact(DisplayName = "GenerateCodesAsync should reject quantity exceeding max")]
    public async Task GenerateCodesAsync_ShouldRejectQuantityExceedingMax()
    {
        // Arrange
        var agent = CreateAgent();

        var request = new GenerateCodesRequestProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProductId = "price_test_monthly",
            Platform = FactoryPaymentPlatform.Stripe,
            TrialDays = 30,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Quantity = 10001, // Exceeds MaxQuantity (10000)
            OperatorUserId = "test-operator-1",
            Description = "Test batch",
            BatchConfig = CreateBatchConfig()
        };

        // Act
        var result = await agent.GenerateCodesAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("exceed");
        result.ErrorCode.ShouldBe((int)FreeTrialCodeError.InternalError);
    }

    [Fact(DisplayName = "GetBatchInfoAsync should return batch information")]
    public async Task GetBatchInfoAsync_ShouldReturnBatchInformation()
    {
        // Arrange
        var agent = CreateAgent();

        // First generate some codes
        var generateRequest = new GenerateCodesRequestProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProductId = "price_test_monthly",
            Platform = FactoryPaymentPlatform.Stripe,
            TrialDays = 30,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Quantity = 3,
            OperatorUserId = "test-operator-1",
            Description = "Test batch",
            BatchConfig = CreateBatchConfig()
        };
        await agent.GenerateCodesAsync(generateRequest);

        // Act
        var result = await agent.GetBatchInfoAsync();

        // Assert
        result.ShouldNotBeNull();
        result.BatchId.ShouldBeGreaterThan(0);
        result.TotalGenerated.ShouldBe(3);
        result.Status.ShouldBe(FactoryStatus.Completed);
        result.GeneratedCodes.ShouldNotBeNull();
        result.GeneratedCodes.Count.ShouldBe(3);
    }

    [Fact(DisplayName = "MarkCodeAsUsedAsync should mark code as used")]
    public async Task MarkCodeAsUsedAsync_ShouldMarkCodeAsUsed()
    {
        // Arrange
        var agent = CreateAgent();

        // First generate codes
        var generateRequest = new GenerateCodesRequestProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProductId = "price_test_monthly",
            Platform = FactoryPaymentPlatform.Stripe,
            TrialDays = 30,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Quantity = 3,
            OperatorUserId = "test-operator-1",
            Description = "Test batch",
            BatchConfig = CreateBatchConfig()
        };
        var generateResult = await agent.GenerateCodesAsync(generateRequest);
        var codeToUse = generateResult.Codes.First();

        // Act
        var markRequest = new MarkCodeUsedRequestProto
        {
            Code = codeToUse,
            UserId = "test-user-1"
        };
        var result = await agent.MarkCodeAsUsedAsync(markRequest);

        // Assert
        result.ShouldBeTrue();
        
        var batchInfo = await agent.GetBatchInfoAsync();
        batchInfo.UsedCount.ShouldBe(1);
        batchInfo.UsedCodes.ShouldContain(codeToUse);
    }

    [Fact(DisplayName = "ValidateCodeOwnershipAsync should validate code ownership")]
    public async Task ValidateCodeOwnershipAsync_ShouldValidateCodeOwnership()
    {
        // Arrange
        var agent = CreateAgent();

        // First generate codes
        var generateRequest = new GenerateCodesRequestProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProductId = "price_test_monthly",
            Platform = FactoryPaymentPlatform.Stripe,
            TrialDays = 30,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Quantity = 3,
            OperatorUserId = "test-operator-1",
            Description = "Test batch",
            BatchConfig = CreateBatchConfig()
        };
        var generateResult = await agent.GenerateCodesAsync(generateRequest);
        var validCode = generateResult.Codes.First();

        // Act - Validate valid code
        var validRequest = new ValidateCodeRequestProto { Code = validCode };
        var isValid = await agent.ValidateCodeOwnershipAsync(validRequest);

        // Act - Validate invalid code
        var invalidRequest = new ValidateCodeRequestProto { Code = "INVALID_CODE" };
        var isInvalid = await agent.ValidateCodeOwnershipAsync(invalidRequest);

        // Assert
        isValid.ShouldBeTrue();
        isInvalid.ShouldBeFalse();
    }

    [Fact(DisplayName = "ValidateCodeAvailableAsync should validate code availability")]
    public async Task ValidateCodeAvailableAsync_ShouldValidateCodeAvailability()
    {
        // Arrange
        var agent = CreateAgent();

        // First generate codes
        var generateRequest = new GenerateCodesRequestProto
        {
            BatchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProductId = "price_test_monthly",
            Platform = FactoryPaymentPlatform.Stripe,
            TrialDays = 30,
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc)),
            Quantity = 3,
            OperatorUserId = "test-operator-1",
            Description = "Test batch",
            BatchConfig = CreateBatchConfig()
        };
        var generateResult = await agent.GenerateCodesAsync(generateRequest);
        var codeToUse = generateResult.Codes.First();

        // Act - Validate unused code
        var unusedRequest = new ValidateCodeRequestProto { Code = codeToUse };
        var isAvailable = await agent.ValidateCodeAvailableAsync(unusedRequest);

        // Mark code as used
        await agent.MarkCodeAsUsedAsync(new MarkCodeUsedRequestProto
        {
            Code = codeToUse,
            UserId = "test-user-1"
        });

        // Act - Validate used code
        var usedRequest = new ValidateCodeRequestProto { Code = codeToUse };
        var isUnavailable = await agent.ValidateCodeAvailableAsync(usedRequest);

        // Assert
        isAvailable.ShouldBeTrue();
        isUnavailable.ShouldBeFalse();
    }
}
