using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Common.Observability;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserQuota;
using Google.Protobuf.WellKnownTypes;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager;

[Description("manage chat agent")]
[GAgent(nameof(ChatGAgentManager))]
[Reentrant]
public partial class ChatGAgentManager : Aevatar.Agents.Core.GAgentBase<ChatManagerStateProto>,
    IChatManagerGAgent
{
    #region Constants
    
    private const string FormattedDate = "yyyy-MM-dd";
    private const string SessionVersion = "1.0.0";
    
    #endregion

    #region Fields
    
    private readonly ILocalizationService _localizationService;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IClusterClient _clusterClient;  // Keep for traditional Orleans Grains (e.g., IShareLinkGrain)
    
    // Cached ConfigurationGAgent interface (new framework)
    private IConfigurationGAgent? _configurationAgentInterface;
    
    #endregion

    #region Configuration Properties (Injected by Orleans Grain)
    
    /// <summary>
    /// Role prompt options - injected by OrleansGAgentGrain via reflection
    /// </summary>
    public RolePromptOptions? RolePromptOptions { get; set; }
    
    #endregion

    #region Constructor
    
    public ChatGAgentManager(
        ILocalizationService localizationService, 
        IGAgentActorFactory actorFactory,
        IClusterClient clusterClient)
    {
        _localizationService = localizationService;
        _actorFactory = actorFactory;
        _clusterClient = clusterClient;
    }
    
    #endregion

    #region Core Methods
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Chat GAgent Manager");
    }
    
    #endregion

    #region Lifecycle
    
    protected override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // Check and initialize first access status if needed
        var firstAccess = await CheckAndInitializeFirstAccessStatus();
        if (firstAccess)
        {
            // Record signup success event via OpenTelemetry
            var userId = Id.ToString();
            UserLifecycleTelemetryMetrics.RecordSignupSuccess(userId: userId, logger: Logger);
        }

        if (State.MaxShareCount == 0)
        {
            RaiseEvent(new SetMaxShareCountEvent
            {
                MaxShareCount = 10000
            });
            await ConfirmEventsAsync();
        }
    }

    /// <summary>
    /// Check and initialize first access status based on version history.
    /// Uses IsFirstConversation field to mark whether this is the first access to ChatManagerGAgent.
    /// If the field has a value, it means this is not the first access.
    /// </summary>
    private async Task<bool> CheckAndInitializeFirstAccessStatus()
    {
        // If IsFirstConversation is already set, no need to set it again
        if (State.IsFirstConversation != null)
        {
            return false;
        }

        // Use Version property to determine if this is a historical user or new user
        // Version > 0 means there are existing events, so it's a historical user
        // Version == 0 means no events yet, so it's a new user
        var isFirstAccess = GetCurrentVersion() == 0;
        var userId = Id;

        if (isFirstAccess)
        {
            // For new users: initialize all fields in one combined event
            RaiseEvent(new InitializeNewUserStatusEvent
            {
                IsFirstConversation = true,
                UserId = userId.ToString(),
                RegisteredAtUtc = DateTime.UtcNow.ToProtoTimestamp(),
                MaxShareCount = 10000
            });
            await ConfirmEventsAsync();
            return true;
        }
        else
        {
            // For historical users: use separate events to maintain backward compatibility
            // Don't set RegisteredAtUtc and MaxShareCount for historical users here
            // as they should be handled by existing logic if needed
            RaiseEvent(new InitializeNewUserStatusEvent
            {
                IsFirstConversation = false,
                UserId = userId.ToString(),
                RegisteredAtUtc = null,
                MaxShareCount = 10000
            });
            await ConfirmEventsAsync();
            return false;
        }
    }
    
    #endregion
}
