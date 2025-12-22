using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent base class with configuration support
/// Provides separate state and configuration persistence
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
/// <typeparam name="TConfig">Agent configuration type</typeparam>
public abstract class GAgentBase<TState, TConfig> : GAgentBase<TState>
    where TState : class, IMessage<TState>, new()
    where TConfig : class, IMessage<TConfig>, new()
{
    public GAgentBase()
    {
    }

    public GAgentBase(string id) : base(id)
    {
    }
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(GetType(), Id, _config, ct);
        }
    }

    private TConfig _config = new();

    /// <summary>
    /// Configuration object - should only be modified within OnActivateAsync or event handlers.
    /// Direct Config assignment is protected, but individual property modifications cannot be intercepted
    /// for Protobuf-generated classes. Follow best practices:
    /// - Only modify Config within OnActivateAsync or [EventHandler] methods
    /// - Use events to trigger configuration changes
    /// - Direct modifications outside these contexts break the Actor model consistency
    /// </summary>
    protected TConfig Config
    {
        get
        {
#if DEBUG
            if (!StateProtectionContext.IsModifiable)
            {
                var callerMethod = new System.Diagnostics.StackFrame(1)?.GetMethod()?.Name ?? "Unknown";
                if (!IsAllowedConfigAccessMethod(callerMethod))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"WARNING: Config accessed from '{callerMethod}' outside protected context. " +
                        "Config should only be modified within OnActivateAsync or event handlers.");
                }
            }
#endif
            return _config;
        }
        set
        {
            StateProtectionContext.EnsureModifiable("Direct Config assignment");
            _config = value;
        }
    }

#if DEBUG
    protected virtual bool IsAllowedConfigAccessMethod(string methodName)
    {
        // Allow certain methods to access Config without warning
        return methodName switch
        {
            nameof(GetConfig) => true,
            nameof(GetDescription) => true,
            nameof(GetDescriptionAsync) => true,
            nameof(OnActivateAsync) => true,
            nameof(ToString) => true,
            nameof(HandleEventAsync) => true,
            nameof(HandleConfigAsync) => true,
            _ => false
        };
    }
#endif

    /// <summary>
    /// Configuration store (injected by Actor layer)
    /// </summary>
    protected IConfigStore<TConfig>? ConfigStore { get; set; }

    /// <summary>
    /// Handle event with automatic configuration and state loading/saving
    /// Extends the parent implementation to add configuration persistence
    /// </summary>
    /// <summary>
    /// Handle event with automatic configuration and state loading/saving
    /// Extends the parent implementation to add configuration persistence
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // Get the actual agent type for configuration isolation
        var agentType = GetType();

        // 1. Load Configuration (if ConfigStore is configured)
        if (ConfigStore != null)
        {
            // Use EventHandlerScope to allow config loading during event handling setup
            using (StateProtectionContext.BeginEventHandlerScope())
            {
                Config = await ConfigStore.LoadAsync(agentType, Id, ct) ?? new TConfig();
            }
        }

        // 2. Call base implementation (Handles State loading, Event Sourcing, Core handling, State saving)
        await base.HandleEventAsync(envelope, ct);

        // 3. Save Configuration (if ConfigStore is configured)
        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(agentType, Id, Config, ct);
        }
    }

    public async Task ConfigAsync(TConfig config)
    {
        await HandleEventAsync(config.CreateEventEnvelope());
    }

    [EventHandler]
    public async Task HandleConfigAsync(TConfig config)
    {
        Config = config;
    }

    /// <summary>
    /// Get configuration (for agents that need to read config)
    /// </summary>
    public TConfig GetConfig() => _config.Clone();
}