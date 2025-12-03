// Compatibility layer for legacy Aevatar framework types
// These types bridge the old MyGet-based framework to the new Aevatar.Agents framework

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

// ============================================================================
// Aevatar.Core.Abstractions namespace
// ============================================================================
namespace Aevatar.Core.Abstractions
{
    /// <summary>
    /// Legacy state base class - now just a marker interface for Orleans serialization
    /// </summary>
    [GenerateSerializer]
    public abstract class StateBase
    {
    }

    /// <summary>
    /// Legacy event log base class for event sourcing
    /// </summary>
    [GenerateSerializer]
    public abstract class StateLogEventBase<TEventLog> where TEventLog : StateLogEventBase<TEventLog>
    {
    }

    /// <summary>
    /// Legacy event base class
    /// </summary>
    [GenerateSerializer]
    public abstract class EventBase
    {
    }
    
    /// <summary>
    /// Legacy IGAgent interface
    /// </summary>
    public interface IGAgent : IGrainWithGuidKey
    {
        Task<string> GetDescriptionAsync();
    }
}

// ============================================================================
// Aevatar.Core namespace
// ============================================================================
namespace Aevatar.Core
{
    /// <summary>
    /// Legacy GAgent attribute for marking GAgent classes
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class GAgentAttribute : Attribute
    {
        public string Name { get; }
        
        public GAgentAttribute(string name)
        {
            Name = name;
        }
        
        // Parameterless constructor for compatibility
        public GAgentAttribute() : this(string.Empty)
        {
        }
    }
    
    /// <summary>
    /// Legacy EventHandler attribute for marking event handler methods
    /// Note: This conflicts with System.EventHandler, so we use full namespace
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class EventHandlerAttribute : Attribute
    {
        public bool AllowSelfHandling { get; set; }
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// Legacy GAgent base class with state and event log support
    /// This bridges to the new framework while maintaining event sourcing patterns
    /// </summary>
    public abstract class GAgentBase<TState, TEventLog> : Grain, IGrainWithGuidKey, Aevatar.Core.Abstractions.IGAgent
        where TState : Aevatar.Core.Abstractions.StateBase, new()
        where TEventLog : Aevatar.Core.Abstractions.StateLogEventBase<TEventLog>
    {
        private readonly List<Aevatar.Core.Abstractions.StateLogEventBase<TEventLog>> _pendingEvents = new();
        private long _eventVersion = 0;
        
        protected TState State { get; private set; } = new();
        protected ILogger Logger => _logger;
        private ILogger _logger = null!;
        
        /// <summary>
        /// Event sourcing version - matches new framework's GetCurrentVersion()
        /// Used to determine if agent has any historical events (Version > 0)
        /// </summary>
        protected long Version => _eventVersion;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger = ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
            await base.OnActivateAsync(cancellationToken);
            await OnGAgentActivateAsync(cancellationToken);
        }

        /// <summary>
        /// Virtual method for GAgent-specific activation logic
        /// </summary>
        protected virtual Task OnGAgentActivateAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Get a description of this GAgent
        /// </summary>
        public abstract Task<string> GetDescriptionAsync();

        /// <summary>
        /// Raise an event for state transition (event sourcing pattern)
        /// </summary>
        protected void RaiseEvent(Aevatar.Core.Abstractions.StateLogEventBase<TEventLog> @event)
        {
            _pendingEvents.Add(@event);
            GAgentTransitionState(State, @event);
        }

        /// <summary>
        /// Confirm pending events (persist to event store)
        /// Updates Version counter to track event sourcing state
        /// </summary>
        protected Task ConfirmEvents()
        {
            _eventVersion += _pendingEvents.Count;
            _pendingEvents.Clear();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Apply state transition based on event - override in derived classes
        /// Default implementation does nothing (for agents that don't use event sourcing)
        /// </summary>
        protected virtual void GAgentTransitionState(TState state, Aevatar.Core.Abstractions.StateLogEventBase<TEventLog> @event)
        {
            // Default implementation does nothing
        }
        
        /// <summary>
        /// Publish event to SignalR/Stream (legacy pattern)
        /// </summary>
        protected virtual Task PublishAsync<T>(T @event) where T : class
        {
            // Stub implementation - actual SignalR publishing would be done here
            Logger.LogDebug($"[PublishAsync] Event published: {typeof(T).Name}");
            return Task.CompletedTask;
        }
    }
}

// ============================================================================
// Aevatar.GAgents.AI.Abstractions namespace
// ============================================================================
namespace Aevatar.GAgents.AI.Abstractions
{
    /// <summary>
    /// Legacy chat message type - matches old framework exactly
    /// </summary>
    [GenerateSerializer]
    public class ChatMessage
    {
        [Id(0)] public string Role { get; set; } = string.Empty;
        [Id(1)] public string Content { get; set; } = string.Empty;
        [Id(2)] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        [Id(3)] public Aevatar.GAgents.ChatAgent.Dtos.ChatRole ChatRole { get; set; }
        [Id(4)] public List<string>? ImageKeys { get; set; }
    }
    
    /// <summary>
    /// Legacy AI GAgent state base
    /// </summary>
    [GenerateSerializer]
    public class AIGAgentStateBase : Aevatar.Core.Abstractions.StateBase
    {
        [Id(0)] public List<ChatMessage> History { get; set; } = new();
        [Id(1)] public string Context { get; set; } = string.Empty;
        [Id(2)] public int TotalTokenUsed { get; set; }
        [Id(3)] public DateTime? LastActivity { get; set; }
        [Id(4)] public string? PromptTemplate { get; set; }
    }
    
    /// <summary>
    /// Legacy chat event log base
    /// </summary>
    [GenerateSerializer]
    public class ChatEventLogBase : Aevatar.Core.Abstractions.StateLogEventBase<ChatEventLogBase>
    {
    }
    
    /// <summary>
    /// Legacy add chat history event
    /// </summary>
    [GenerateSerializer]
    public class AddChatHistoryLogEvent : ChatEventLogBase
    {
        [Id(0)] public ChatMessage Message { get; set; } = new();
    }
}

// ============================================================================
// Aevatar.GAgents.ChatAgent.GAgent.State namespace
// ============================================================================
namespace Aevatar.GAgents.ChatAgent.GAgent.State
{
    /// <summary>
    /// Legacy Chat GAgent state base - used by GodChatState
    /// </summary>
    [GenerateSerializer]
    public class ChatGAgentState : Aevatar.Core.Abstractions.StateBase
    {
        [Id(0)] public List<Aevatar.GAgents.AI.Abstractions.ChatMessage> ChatHistory { get; set; } = new();
        [Id(1)] public int MaxHistoryCount { get; set; } = 50;
        [Id(2)] public string? PromptTemplate { get; set; }
    }
}

// ============================================================================
// Aevatar.GAgents.AIGAgent namespace
// ============================================================================
namespace Aevatar.GAgents.AIGAgent
{
    /// <summary>
    /// Legacy AI GAgent base class with 4 type parameters
    /// Note: Parameter order is <TState, TEventLog, TConfig, TEvent>
    /// TConfig is the 3rd parameter, TEvent is the 4th parameter
    /// </summary>
    public abstract class AIGAgentBase<TState, TEventLog, TConfig, TEvent> : Aevatar.Core.GAgentBase<TState, TEventLog>
        where TState : Aevatar.Core.Abstractions.StateBase, new()
        where TEventLog : Aevatar.Core.Abstractions.StateLogEventBase<TEventLog>
        where TConfig : class
        where TEvent : class
    {
        protected TConfig? AIConfig { get; set; }
        protected TEvent? EventConfig { get; set; }
        
        // Stream-related properties for Orleans Stream compatibility
        // Uses new framework's stream namespace constant
        private const string DefaultStreamProviderName = "AevatarAgents";
        private const string DefaultStreamNamespace = "AevatarAgents";
        
        protected Orleans.Streams.IStreamProvider? StreamProvider => 
            TryGetStreamProvider(DefaultStreamProviderName);
            
        protected Aevatar.GAgents.AI.Options.StreamingConfig AevatarOptions { get; set; } = 
            new Aevatar.GAgents.AI.Options.StreamingConfig { StreamNamespace = DefaultStreamNamespace };
        
        private Orleans.Streams.IStreamProvider? TryGetStreamProvider(string name)
        {
            try
            {
                return this.GetStreamProvider(name);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Perform configuration with TEvent type - override in derived classes
        /// Note: In old framework, TEvent (4th param) was used for config in some cases
        /// </summary>
        protected virtual Task PerformConfigAsync(TEvent configuration)
        {
            EventConfig = configuration;
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Initialize the AI agent
        /// </summary>
        public virtual Task InitializeAsync(Aevatar.GAgents.AIGAgent.Dtos.InitializeDto initDto)
        {
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Chat with the AI agent
        /// </summary>
        public virtual Task<List<Aevatar.GAgents.AI.Abstractions.ChatMessage>?> ChatAsync(string prompt)
        {
            return Task.FromResult<List<Aevatar.GAgents.AI.Abstractions.ChatMessage>?>(null);
        }
        
        /// <summary>
        /// Chat with history
        /// </summary>
        protected virtual Task<List<Aevatar.GAgents.AI.Abstractions.ChatMessage>?> ChatWithHistory(
            string prompt, 
            List<Aevatar.GAgents.AI.Abstractions.ChatMessage>? history = null,
            Aevatar.GAgents.AI.Options.ExecutionPromptSettings? promptSettings = null, 
            Aevatar.GAgents.AIGAgent.Dtos.AIChatContextDto? context = null)
        {
            return Task.FromResult<List<Aevatar.GAgents.AI.Abstractions.ChatMessage>?>(null);
        }
        
        /// <summary>
        /// Prompt with streaming
        /// </summary>
        protected virtual Task<bool> PromptWithStreamAsync(
            string prompt, 
            List<Aevatar.GAgents.AI.Abstractions.ChatMessage>? history = null,
            Aevatar.GAgents.AI.Options.ExecutionPromptSettings? promptSettings = null, 
            Aevatar.GAgents.AIGAgent.Dtos.AIChatContextDto? context = null,
            List<string>? imageKeys = null)
        {
            return Task.FromResult(false);
        }
        
        /// <summary>
        /// Handle stream response - override in derived classes
        /// </summary>
        protected virtual Task AIChatHandleStreamAsync(
            Aevatar.GAgents.AIGAgent.Dtos.AIChatContextDto? context, 
            Aevatar.GAgents.AI.Common.AIExceptionEnum errorEnum,
            string? errorMessage,
            Aevatar.GAgents.AI.Common.AIStreamChatContent? content)
        {
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// AI-specific state transition - override in derived classes
        /// </summary>
        protected virtual void AIGAgentTransitionState(TState state, Aevatar.Core.Abstractions.StateLogEventBase<TEventLog> @event)
        {
            // Default implementation does nothing
        }
        
        /// <summary>
        /// Override base class transition to call AI-specific transition
        /// </summary>
        protected sealed override void GAgentTransitionState(TState state, Aevatar.Core.Abstractions.StateLogEventBase<TEventLog> @event)
        {
            AIGAgentTransitionState(state, @event);
        }
    }
}

// ============================================================================
// Aevatar.Core namespace - 4 parameter GAgentBase for ChatAgent
// ============================================================================
namespace Aevatar.Core
{
    /// <summary>
    /// Legacy GAgent base class with 4 type parameters (for ChatAgent compatibility)
    /// Now supports Protobuf config types (IMessage&lt;TConfig&gt;)
    /// </summary>
    public abstract class GAgentBase<TState, TEventLog, TEvent, TConfig> : GAgentBase<TState, TEventLog>
        where TState : Aevatar.Core.Abstractions.StateBase, new()
        where TEventLog : Aevatar.Core.Abstractions.StateLogEventBase<TEventLog>
        where TEvent : class
        where TConfig : class, new()
    {
        protected TConfig Config { get; set; } = new();
        
        /// <summary>
        /// Perform configuration - override in derived classes
        /// Migrated to support Protobuf config types
        /// </summary>
        protected virtual Task PerformConfigAsync(TConfig configuration)
        {
            Config = configuration;
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Configure the agent (public interface method)
        /// Calls PerformConfigAsync for backward compatibility
        /// </summary>
        public virtual Task ConfigAsync(TConfig config)
        {
            return PerformConfigAsync(config);
        }
    }
}

// NOTE: ChatGAgentBase, IStreamingResponse, ExecutionOptions removed - unused
