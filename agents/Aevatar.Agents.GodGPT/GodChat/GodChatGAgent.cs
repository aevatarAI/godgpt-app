using System.Diagnostics;
using System.Text;
using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.GEvents;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using Aevatar.Application.Grains.GodChat;
using GodGPT.GAgents.Common.Constants;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans;
using Orleans.Concurrency;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BlobStoring;
using Volo.Abp.Threading;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

[Description("god chat agent")]
[GAgent(nameof(GodChatGAgent))]
public partial class GodChatGAgent : Aevatar.Agents.Core.GAgentBase<GodChatStateProto, GodChatConfig>, IGodChat
{
    #region Constants
    
    private static readonly TimeSpan RequestRecoveryDelay = TimeSpan.FromSeconds(600);
    private const string DefaultRegion = "DEFAULT";
    private const string CNDefaultRegion = "CN";
    private const string CNConsoleRegion = "CNCONSOLE";
    private const string ConsoleRegion = "CONSOLE";
    private const string LocalBackupModel = "OpenAI";
    private const string DailyGuide = $"###{SessionGuiderConstants.DailyGuide}###";
    private const string StreamNamespace = "AevatarAgents";
    private const string StreamProviderName = "AevatarAgents";
    
    #endregion

    #region Fields
    
    private readonly ISpeechService _speechService;
    private readonly IOptionsMonitor<LLMRegionOptions> _llmRegionOptions;
    private readonly ILocalizationService _localizationService;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IClusterClient _clusterClient;
    
    // Cached ConfigurationGAgent instance (new framework)
    private ConfigurationGAgent? _configurationAgent;
    
    // Dictionary to maintain text accumulator for voice chat sessions
    // Key: chatId, Value: accumulated text buffer for sentence detection
    private static readonly Dictionary<string, StringBuilder> VoiceTextAccumulators = new();
    
    // Instance variables for suggestion filtering state management
    // These persist across chunk processing within the same grain instance
    private bool _isAccumulatingForSuggestions = false;
    private string _accumulatedSuggestionContent = "";
    
    #endregion

    #region Constructor
    
    public GodChatGAgent(
        ISpeechService speechService, 
        IOptionsMonitor<LLMRegionOptions> llmRegionOptions, 
        ILocalizationService localizationService, 
        IGAgentActorFactory actorFactory,
        IClusterClient clusterClient)
    {
        _speechService = speechService;
        _llmRegionOptions = llmRegionOptions;
        _localizationService = localizationService;
        _actorFactory = actorFactory;
        _clusterClient = clusterClient;
    }
    
    #endregion

    #region Configuration
    
    /// <summary>
    /// New framework ConfigAsync - replaces PerformConfigAsync
    /// </summary>
    public new async Task ConfigAsync(GodChatConfig configuration)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[GodChatGAgent][ConfigAsync] Start - SessionId: {Id}");
        
        var regionToLLMsMap = _llmRegionOptions.CurrentValue.RegionToLLMsMap;
        if (regionToLLMsMap.IsNullOrEmpty())
        {
            Logger.LogDebug($"[GodChatGAgent][ConfigAsync] LLMConfigs is null or empty.");
            return;
        }
        var isCN = GodGPTLanguageHelper.CheckClientIsCNFromContext();
        var defaultRegion = DefaultRegion;
        if (isCN)
        {
            defaultRegion = CNDefaultRegion;
        }
        Logger.LogDebug(
            $"[GodChatGAgent][InitializeRegionProxiesAsync] session {Id.ToString()},isCN:{isCN}, region:{defaultRegion}");

        var proxyIds = await InitializeRegionProxiesAsync(defaultRegion, configuration.Instructions);
        
        // Optimize: Use combined event to reduce RaiseEvent calls from 3 to 1
        var maxHistoryCount = configuration.MaxHistoryCount;
        if (maxHistoryCount > 100)
        {
            maxHistoryCount = 100;
        }

        if (maxHistoryCount == 0)
        {
            maxHistoryCount = 10;
        }
        
        // Use Protobuf event
        RaiseEvent(new PerformConfigCombinedEvent
        {
            Region = defaultRegion,
            ProxyIds = { proxyIds.Select(g => g.ToString()) },
            PromptTemplate = configuration.Instructions ?? "",
            MaxHistoryCount = maxHistoryCount
        });

        await ConfirmEventsAsync();
        
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][ConfigAsync] End - Total Duration: {stopwatch.ElapsedMilliseconds}ms, SessionId: {Id}");
    }

    public override Task<string> GetDescriptionAsync()
    {
        throw new NotImplementedException();
    }

    public async Task InitAsync(Guid ChatManagerGuid)
    {
        Logger.LogDebug($"[GodChatGAgent][InitAsync] Start - SessionId: {Id}, ChatManagerGuid: {ChatManagerGuid}");
        
        RaiseEvent(new SetChatManagerGuidEvent
        {
            ChatManagerGuid = ChatManagerGuid.ToString()
        });

        Logger.LogDebug($"[GodChatGAgent][InitAsync] End -  SessionId: {Id}");
    }
    
    #endregion

    #region User Profile
    
    public async Task SetUserProfileAsync(UserProfileDto? userProfileDto)
    {
        if (userProfileDto == null)
        {
            return;
        }

        RaiseEvent(new UpdateUserProfileEvent
        {
            Gender = userProfileDto.Gender,
            BirthDate = Timestamp.FromDateTime(DateTime.SpecifyKind(userProfileDto.BirthDate, DateTimeKind.Utc)),
            BirthPlace = userProfileDto.BirthPlace,
            FullName = userProfileDto.FullName
        });

        await ConfirmEventsAsync();
    }

    public async Task<UserProfileDto?> GetUserProfileAsync()
    {
        if (State.UserProfile == null)
        {
            return null;
        }

        return new UserProfileDto
        {
            Gender = State.UserProfile.Gender,
            BirthDate = State.UserProfile.BirthDate?.ToDateTime() ?? DateTime.MinValue,
            BirthPlace = State.UserProfile.BirthPlace,
            FullName = State.UserProfile.FullName
        };
    }
    
    #endregion

    #region Message Retrieval
    
    public Task<List<ChatMessage>> GetChatMessageAsync()
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][GetSessionMessageListAsync] - session:ID {Id.ToString()} ,message={JsonConvert.SerializeObject(State.ChatHistory)}");
        return Task.FromResult(State.ChatHistory.FromProtoList());
    }

    public Task<List<ChatMessageWithMetaDto>> GetChatMessageWithMetaAsync()
    {
        Logger.LogDebug(
            $"[GodChatGAgent][GetChatMessageWithMetaAsync] - sessionId: {Id}, messageCount: {State.ChatHistory.Count}, metaCount: {State.ChatMessageMetas.Count}");

        var result = new List<ChatMessageWithMetaDto>();

        // Combine ChatHistory with ChatMessageMetas
        for (int i = 0; i < State.ChatHistory.Count; i++)
        {
            var message = State.ChatHistory[i].FromProto();
            var meta = i < State.ChatMessageMetas.Count ? State.ChatMessageMetas[i].FromProto() : null;

            result.Add(ChatMessageWithMetaDto.Create(message, meta));
        }

        Logger.LogDebug(
            $"[GodChatGAgent][GetChatMessageWithMetaAsync] - sessionId: {Id}, returned {result.Count} messages with metadata");

        return Task.FromResult(result);
    }

    public Task<DateTime?> GetFirstChatTimeAsync()
    {
        return Task.FromResult(State.FirstChatTime?.ToDateTime());
    }

    public Task<DateTime?> GetLastChatTimeAsync()
    {
        return Task.FromResult(State.LastChatTime?.ToDateTime());
    }
    
    #endregion
}
