using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Layer 0: Core AI Agent Base
/// - Manages LLM interactions
/// - Manages Standard State (History, Token Usage, etc)
/// - Manages Standard Config
/// </summary>
public abstract class AIGAgentBase : GAgentBase<AevatarAIAgentState, AevatarAIAgentConfig>
{
    #region Fields

    protected IAevatarLLMProvider? _llmProvider;
    protected bool _isInitialized;
    protected ILLMProviderFactory LLMProviderFactory { get; set; }
    protected IAIAgentEmbeddingFactory? EmbeddingFactory { get; set; }
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private LLMProviderConfig? _activeProviderConfig;

    #endregion

    public AIGAgentBase()
    {
    }

    public AIGAgentBase(string id) : base(id)
    {
    }

    #region Properties

    /// <summary>
    /// System prompt for the AI agent.
    /// </summary>
    public virtual string SystemPrompt { get; set; } = "You are a helpful AI assistant.";

    [field: AllowNull, MaybeNull]
    protected ConversationHistoryManager ConversationHistory =>
        field ??= CreateConversationHistoryManager();

    protected virtual ConversationHistoryManager CreateConversationHistoryManager()
    {
        return new ConversationHistoryManager(State.History);
    }

    protected virtual void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
    {
        ConversationHistory.AddMessage(content, role, name);
    }

    protected virtual void AddMessageToHistory(AevatarChatMessage message)
    {
        ConversationHistory.AddMessage(message);
    }

    /// <summary>
    /// Gets the LLM provider.
    /// </summary>
    public IAevatarLLMProvider LLMProvider
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "AI Agent must be initialized before use. Call InitializeAsync() first.");
            return _llmProvider!;
        }
    }

    protected bool HasEmbeddingGenerator => _embeddingGenerator != null;
    protected LLMProviderConfig? ActiveProviderConfig => _activeProviderConfig;

    protected IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator =>
        _embeddingGenerator ?? throw new InvalidOperationException(
            "Embedding generator is not configured. Ensure LLM provider Embeddings settings are provided and IAIAgentEmbeddingFactory is registered.");

    #endregion

    #region Initialization

    /// <summary>
    /// Helper method to load and configure state and configuration during initialization.
    /// Must be called within an InitializationScope.
    /// </summary>
    protected virtual async Task InitializeStateAndConfigAsync(
        Action<AevatarAIAgentConfig>? configAI,
        CancellationToken cancellationToken)
    {
        // NOTE: Do NOT call ActivateAsync() here!
        // This method is typically called from OnActivateAsync, 
        // calling ActivateAsync again would cause infinite recursion.

        // Load state and config if stores are available
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, cancellationToken) ?? new AevatarAIAgentState();
        }

        var agentType = GetType();
        if (ConfigStore != null)
        {
            Config = await ConfigStore.LoadAsync(agentType, Id, cancellationToken) ?? new AevatarAIAgentConfig();
        }

        // Configure AI settings
        ConfigAI(Config);
        configAI?.Invoke(Config);

        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(agentType, Id, Config, cancellationToken);
        }
    }

    /// <summary>
    /// Initialize the AI agent with a named LLM provider from ASP.NET Options.
    /// This method must be called before using the agent.
    /// </summary>
    /// <param name="providerName">Name of the LLM provider from appsettings.json (e.g., "openai-gpt4", "azure-gpt35")</param>
    /// <param name="configAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await InitializeStateAndConfigAsync(configAI, cancellationToken);

        _activeProviderConfig = LLMProviderFactory.GetProviderConfig(providerName);

        // Create LLM Provider from factory using provider name
        _llmProvider = await CreateLLMProviderFromFactoryAsync(providerName, cancellationToken);

        await InitializeEmbeddingGeneratorAsync(_activeProviderConfig, cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with LLM provider '{ProviderName}'", Id, providerName);
    }

    /// <summary>
    /// Initialize the AI agent with custom LLM provider configuration.
    /// This method must be called before using the agent.
    /// </summary>
    /// <param name="providerConfig">Custom LLM provider configuration</param>
    /// <param name="configAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await InitializeStateAndConfigAsync(configAI, cancellationToken);

        _activeProviderConfig = providerConfig;

        // Create LLM Provider from custom config
        _llmProvider = await CreateLLMProviderFromConfigAsync(providerConfig, cancellationToken);

        await InitializeEmbeddingGeneratorAsync(_activeProviderConfig, cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with custom LLM provider '{ProviderType}'",
            Id, providerConfig.ProviderType);
    }

    #endregion

    #region LLM Provider Creation

    /// <summary>
    /// Creates LLM Provider from factory using provider name.
    /// </summary>
    protected virtual async Task<IAevatarLLMProvider> CreateLLMProviderFromFactoryAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        if (LLMProviderFactory == null)
        {
            throw new InvalidOperationException(
                "ILLMProviderFactory is not available. " +
                "Use the constructor that accepts ILLMProviderFactory or override this method.");
        }

        // Get provider from factory
        return await LLMProviderFactory.GetProviderAsync(providerName, cancellationToken);
    }

    /// <summary>
    /// Creates LLM Provider from custom configuration.
    /// 从自定义配置创建LLM Provider
    /// </summary>
    protected virtual async Task<IAevatarLLMProvider> CreateLLMProviderFromConfigAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken)
    {
        if (LLMProviderFactory == null)
        {
            throw new InvalidOperationException(
                "ILLMProviderFactory is not available. " +
                "Use the constructor that accepts ILLMProviderFactory or override this method.");
        }

        // Create provider from config using factory
        return LLMProviderFactory.CreateProvider(providerConfig, cancellationToken);
    }

    #endregion

    #region Embeddings

    protected bool TryGetEmbeddingGenerator(
        [NotNullWhen(true)] out IEmbeddingGenerator<string, Embedding<float>>? generator)
    {
        generator = _embeddingGenerator;
        return generator != null;
    }

    protected virtual async Task InitializeEmbeddingGeneratorAsync(
        LLMProviderConfig? providerConfig,
        CancellationToken cancellationToken)
    {
        if (providerConfig is not { Embeddings.Enabled: true })
        {
            return;
        }

        if (EmbeddingFactory == null)
        {
            Logger.LogDebug("EmbeddingFactory not available, skipping embedding initialization for provider {Provider}",
                providerConfig.Name);
            return;
        }

        try
        {
            var generator = await EmbeddingFactory.CreateAsync(providerConfig, cancellationToken);
            if (generator != null)
            {
                _embeddingGenerator = generator;
                Logger.LogInformation("Embedding generator initialized for provider {Provider}", providerConfig.Name);
            }
            else
            {
                Logger.LogDebug("EmbeddingFactory returned null for provider {Provider}", providerConfig.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to initialize embedding generator for provider {Provider}", providerConfig.Name);
        }
    }

    protected virtual async Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_embeddingGenerator == null)
            throw new InvalidOperationException(
                "Embedding generator is not configured. Ensure Embeddings settings exist in the provider configuration.");

        var generationOptions = options ?? BuildDefaultEmbeddingOptions();
        var embeddings = await _embeddingGenerator.GenerateAsync(inputs, generationOptions, cancellationToken);
        return embeddings;
    }

    protected virtual async Task<Embedding<float>?> GenerateEmbeddingAsync(
        string input,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await GenerateEmbeddingsAsync(new[] { input }, options, cancellationToken);
        return embeddings.Count > 0 ? embeddings[0] : null;
    }

    protected virtual EmbeddingGenerationOptions BuildDefaultEmbeddingOptions()
    {
        var options = new EmbeddingGenerationOptions();
        if (_activeProviderConfig?.Embeddings != null)
        {
            if (!string.IsNullOrWhiteSpace(_activeProviderConfig.Embeddings.Model))
            {
                options.ModelId = _activeProviderConfig.Embeddings.Model;
            }
            else if (!string.IsNullOrWhiteSpace(_activeProviderConfig.Model))
            {
                options.ModelId = _activeProviderConfig.Model;
            }

            if (_activeProviderConfig.Embeddings.Dimensions.HasValue)
            {
                options.Dimensions = _activeProviderConfig.Embeddings.Dimensions;
            }
        }
        else if (!string.IsNullOrWhiteSpace(_activeProviderConfig?.Model))
        {
            options.ModelId = _activeProviderConfig.Model;
        }

        return options;
    }

    protected static double CosineSimilarity(Embedding<float> left, Embedding<float> right)
    {
        var leftSpan = left.Vector.Span;
        var rightSpan = right.Vector.Span;

        if (leftSpan.Length != rightSpan.Length)
            throw new InvalidOperationException("Embedding dimensions must match to calculate cosine similarity.");

        double dot = 0;
        double magLeft = 0;
        double magRight = 0;

        for (var i = 0; i < leftSpan.Length; i++)
        {
            var l = leftSpan[i];
            var r = rightSpan[i];
            dot += l * r;
            magLeft += l * l;
            magRight += r * r;
        }

        if (magLeft == 0 || magRight == 0)
            return 0;

        return dot / (Math.Sqrt(magLeft) * Math.Sqrt(magRight));
    }

    #endregion

    #region Configuration Methods

    /// <summary>
    /// Configure AI settings. Override in derived classes.
    /// </summary>
    protected virtual void ConfigAI(AevatarAIAgentConfig config)
    {
        // Set defaults from centralized constants
        config.Model = AevatarAIDefaults.DefaultModel;
        config.Temperature = AevatarAIDefaults.DefaultTemperature;
        config.MaxOutputTokens = AevatarAIDefaults.DefaultMaxOutputTokens;

        // Override in derived classes
    }

    #endregion

    #region Chat Methods

    /// <summary>
    /// Process a chat request and return a response.
    /// 处理聊天请求并返回响应
    /// </summary>
    /// <param name="request">Chat request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chat response</returns>
    public virtual async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        try
        {
            // Build LLM request from chat request
            var llmRequest = BuildLLMRequest(request);

            // Call LLM
            var llmResponse = await LLMProvider.GenerateAsync(llmRequest, cancellationToken);

            // Build chat response
            var response = new ChatResponse
            {
                Content = llmResponse.Content,
                RequestId = request.RequestId
            };

            // Add token usage if available
            if (llmResponse.Usage != null)
            {
                response.Usage = new AevatarTokenUsage
                {
                    PromptTokens = llmResponse.Usage.PromptTokens,
                    CompletionTokens = llmResponse.Usage.CompletionTokens,
                    TotalTokens = llmResponse.Usage.TotalTokens
                };
            }

            // Publish chat response event
            await PublishAsync(new ChatResponseEvent
            {
                RequestId = request.RequestId,
                Content = response.Content,
                TokensUsed = response.Usage?.TotalTokens ?? 0,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }, ct: cancellationToken);

            // Record the AI decision as an event (Event Sourcing)
            RaiseAIDecision(
                request.Message,
                response.Content,
                response.Usage?.TotalTokens ?? 0,
                new Dictionary<string, string>
                {
                    ["request_id"] = request.RequestId,
                    ["chat_type"] = "sync"
                });

            // Auto-confirm if configured and EventStore is present
            if (AutoConfirmEvents && EventStore != null)
            {
                await ConfirmEventsAsync(cancellationToken);
            }

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing chat request {RequestId}", request.RequestId);
            throw;
        }
    }

    /// <summary>
    /// Build LLM request from chat request.
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequest(
        ChatRequest request)
    {
        var settings = GetLLMSettings(request);
        if (request.StopSequences.Count > 0)
        {
            settings.StopSequences = request.StopSequences.ToList();
        }

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = GetEffectiveSystemPrompt(),
            Messages = new List<AevatarChatMessage>
            {
                new()
                {
                    Role = AevatarChatRole.User,
                    Content = request.Message
                }
            },
            Settings = settings
        };

        if (!string.IsNullOrWhiteSpace(request.StageHint))
        {
            llmRequest.Context = new Dictionary<string, object>
            {
                ["stage_hint"] = request.StageHint!
            };
        }

        return llmRequest;
    }

    /// <summary>
    /// Determine the effective system prompt, preferring configuration override.
    /// </summary>
    protected virtual string? GetEffectiveSystemPrompt()
    {
        return !string.IsNullOrWhiteSpace(Config.SystemPrompt)
            ? Config.SystemPrompt
            : SystemPrompt;
    }

    /// <summary>
    /// Get LLM settings from chat request.
    /// 从聊天请求获取 LLM 设置
    /// </summary>
    protected virtual AevatarLLMSettings GetLLMSettings(ChatRequest request)
    {
        // Use request values if provided (considering 0 as a valid temperature), otherwise use configuration
        // For temperature: accept any value >= 0 as valid override
        // For maxTokens: only positive values are valid overrides
        var temperature = request.Temperature >= 0 ? request.Temperature : Config.Temperature;
        var maxTokens = request.MaxTokens > 0 ? request.MaxTokens : Config.MaxOutputTokens;

        return new AevatarLLMSettings
        {
            Temperature = temperature,
            MaxTokens = maxTokens,
            ModelId = Config.Model
        };
    }

    /// <summary>
    /// Create a chat request with the given message.
    /// 使用给定的消息创建聊天请求
    /// </summary>
    public virtual ChatRequest CreateChatRequest(string message)
    {
        return ChatRequest.Create(message);
    }

    /// <summary>
    /// Generate a response to a message (convenience method).
    /// 生成消息的响应（便捷方法）
    /// </summary>
    public virtual Task<ChatResponse> GenerateResponseAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatAsync(request, cancellationToken);
    }

    /// <summary>
    /// Generate a streaming response to a chat request.
    /// 生成聊天请求的流式响应
    /// </summary>
    public virtual async IAsyncEnumerable<string> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        // Build LLM request
        var llmRequest = BuildLLMRequest(request);

        // Stream from LLM
        var enumerator = LLMProvider.GenerateStreamAsync(llmRequest, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                string? content;
                bool isComplete;

                try
                {
                    var hasNext = await enumerator.MoveNextAsync();
                    if (!hasNext) break;

                    var token = enumerator.Current;
                    content = token.Content;
                    isComplete = token.IsComplete;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in streaming chat request {RequestId}", request.RequestId);
                    throw;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }

                if (isComplete)
                    break;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    /// <summary>
    /// Generate a streaming response to a message (convenience method).
    /// </summary>
    public virtual IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatStreamAsync(request, cancellationToken);
    }

    /// <summary>
    /// Check if the LLM provider supports streaming.
    /// </summary>
    public virtual async Task<bool> SupportsStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            return false;

        var modelInfo = await LLMProvider.GetModelInfoAsync(cancellationToken);
        return modelInfo.SupportsStreaming;
    }

    #endregion

    #region AI Event Sourcing Support

    /// <summary>
    /// Auto-confirm events after AI operations.
    /// Defaults to false. Override to enable.
    /// </summary>
    protected virtual bool AutoConfirmEvents => false;

    /// <summary>
    /// Record an AI decision as an event.
    /// </summary>
    protected void RaiseAIDecision(
        string prompt,
        string response,
        int tokensUsed,
        Dictionary<string, string>? metadata = null)
    {
        var aiEvent = new AIDecisionEvent
        {
            Prompt = prompt,
            Response = response,
            TokensUsed = tokensUsed,
            Model = Config.Model,
            Temperature = Config.Temperature,
            Timestamp = TimestampHelper.GetUtcNow()
        };

        // Add AI-specific metadata
        var eventMetadata = metadata ?? new Dictionary<string, string>();
        eventMetadata["ai_model"] = Config.Model;
        eventMetadata["ai_temperature"] = Config.Temperature.ToString(CultureInfo.InvariantCulture);

        RaiseEvent(aiEvent, eventMetadata);
    }

    /// <summary>
    /// Pure functional state transition for AI Agent.
    /// </summary>
    protected override void TransitionState(AevatarAIAgentState state, IMessage evt)
    {
        // Default implementation handles standard AI events
        // Users can override to handle custom events
        switch (evt)
        {
            case AIDecisionEvent aiEvent:
                // Update state with AI decision if needed
                // For now, standard state might not need to track every decision,
                // but we can add it to history if we want.
                // The standard AevatarAIAgentState might have a history field.
                break;
            case ChatResponseEvent chatEvent:
                // Already handled by ChatAsync return value, but maybe we want to update history here?
                break;
        }
    }

    #endregion
}