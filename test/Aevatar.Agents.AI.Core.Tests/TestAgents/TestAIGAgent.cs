using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Tests.Messages;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.Core.Tests.TestAgents;

/// <summary>
/// Simplified test implementation of AIGAgentBase for unit testing
/// </summary>
// ReSharper disable InconsistentNaming
public class TestAIGAgent(string? id = null) : AIGAgentBase<TestAIGAgentState, TestAIGAgentConfig>(id ?? Guid.NewGuid().ToString())
{
    /// <summary>
    /// Track initialization calls for testing
    /// </summary>
    public int InitializeCallCount { get; private set; }

    /// <summary>
    /// Track configuration calls for testing
    /// </summary>
    public int ConfigureAICallCount { get; private set; }

    /// <summary>
    /// Custom system prompt for testing
    /// </summary>
    public string TestSystemPrompt { get; set; } = "Test AI Assistant";

    /// <summary>
    /// Override system prompt
    /// </summary>
    public override string SystemPrompt
    {
        get => TestSystemPrompt;
        set => TestSystemPrompt = value;
    }

    /// <summary>
    /// Override initialization to track calls
    /// </summary>
    public override async Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        InitializeCallCount++;
        await base.InitializeAsync(providerName, configAI, cancellationToken);
    }

    /// <summary>
    /// Override initialization with custom config to track calls
    /// </summary>
    public override async Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        InitializeCallCount++;
        await base.InitializeAsync(providerConfig, configAI, cancellationToken);
    }

    /// <summary>
    /// Override AI configuration to track calls
    /// </summary>
    protected override void ConfigAI(AevatarAIAgentConfig config)
    {
        ConfigureAICallCount++;

        // Set test defaults
        config.Model = "test-model";
        config.Temperature = 0.5f;
        config.MaxOutputTokens = 1000;
    }

    /// <summary>
    /// Override description for testing
    /// </summary>
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"TestAIGAgent (Messages: {CustomState.MessageCount})");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Set test defaults
        CustomConfig.ConfigId = "test-config";
        CustomConfig.Description = "Test configuration";
        CustomConfig.MaxRetries = 3;
        CustomConfig.TimeoutSeconds = 30.0;
        CustomConfig.EnableLogging = true;
        CustomConfig.BatchSize = 10;
        CustomConfig.AllowedOperations.Add("read");
        CustomConfig.AllowedOperations.Add("write");
        CustomConfig.CustomSettings["test-key"] = "test-value";

        await base.OnActivateAsync(ct);
    }

    /// <summary>
    /// Test helper to check if initialized
    /// </summary>
    public bool IsInitialized => LLMProviderFactory != null;

    public bool HasEmbeddings => HasEmbeddingGenerator;

    public Task<Embedding<float>?> GenerateEmbeddingForTestAsync(string input, CancellationToken ct = default)
    {
        return GenerateEmbeddingAsync(input, cancellationToken: ct);
    }

    /// <summary>
    /// Test helper to reset counters
    /// </summary>
    public void ResetCounters()
    {
        InitializeCallCount = 0;
        ConfigureAICallCount = 0;
    }

    /// <summary>
    /// Test helper to update state (simulates event handler context)
    /// </summary>
    public Task UpdateStateInContextAsync(Action<TestAIGAgentState> updateAction)
    {
        // Simulate being in an event handler context
        return Task.Run(() => { updateAction(CustomState); });
    }
}
