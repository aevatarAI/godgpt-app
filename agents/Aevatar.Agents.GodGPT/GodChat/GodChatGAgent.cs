using System.Diagnostics;
using System.Text;
using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
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
[Reentrant]
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
    
    // Cached ConfigurationGAgent interface (new framework)
    private IConfigurationGAgent? _configurationAgentInterface;
    
    // Dictionary to maintain text accumulator for voice chat sessions
    // Key: chatId, Value: accumulated text buffer for sentence detection
    private static readonly Dictionary<string, StringBuilder> VoiceTextAccumulators = new();
    
    // Instance variables for suggestion filtering state management
    // These persist across chunk processing within the same grain instance
    private bool _isAccumulatingForSuggestions = false;
    private string _accumulatedSuggestionContent = "";
    
    // Message aggregation for reducing Kafka message count
    // Aggregates multiple streaming tokens into fewer messages
    // For AI chat, slightly larger values reduce Kafka pressure without noticeable latency impact
    private readonly StringBuilder _messageAggregationBuffer = new();
    private ResponseStreamGodChat? _pendingAggregatedMessage = null;
    private DateTime _lastMessageSentTime = DateTime.MinValue;
    private const int MessageAggregationIntervalMs = 150; // Send aggregated message every 150ms
    private const int MaxAggregatedTokens = 15; // Or after 15 tokens, whichever comes first
    private int _aggregatedTokenCount = 0;
    
    #endregion

    #region Injected Properties (for Orleans Grain mode)
    
    /// <summary>
    /// ServiceProvider injected by Orleans Grain for Stream access
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }
    
    #endregion

    #region Constructor
    
    public GodChatGAgent(
        ISpeechService speechService, 
        IOptionsMonitor<LLMRegionOptions> llmRegionOptions, 
        ILocalizationService localizationService, 
        IGAgentActorFactory actorFactory)
    {
        _speechService = speechService;
        _llmRegionOptions = llmRegionOptions;
        _localizationService = localizationService;
        _actorFactory = actorFactory;
    }
    
    #endregion

    #region Configuration
    
    /// <summary>
    /// New framework ConfigAsync - replaces PerformConfigAsync
    /// ✅ 优化：延迟初始化 Proxy，不在 ConfigAsync 中创建
    /// Proxy 会在第一次聊天时通过 GetProxyByRegionAsync 懒加载
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
        var isCN = GodGPTLanguageHelper.CheckClientIsCN(Context);
        var defaultRegion = DefaultRegion;
        if (isCN)
        {
            defaultRegion = CNDefaultRegion;
        }
        Logger.LogDebug(
            $"[GodChatGAgent][ConfigAsync] session {Id.ToString()}, isCN:{isCN}, region:{defaultRegion} (Proxy will be lazy-loaded on first chat)");

        // ✅ 延迟初始化：不再在这里创建 Proxy
        // Proxy 会在第一次聊天调用 GetProxyByRegionAsync 时懒加载
        // var proxyIds = await InitializeRegionProxiesAsync(defaultRegion, configuration.Instructions);
        
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
        
        // Use Protobuf event - 保存配置，但不创建 Proxy
        RaiseEvent(new PerformConfigCombinedEvent
        {
            Region = defaultRegion,
            ProxyIds = { }, // ✅ 空列表，Proxy 会懒加载
            PromptTemplate = configuration.Instructions ?? "",
            MaxHistoryCount = maxHistoryCount
        });

        await ConfirmEventsAsync();
        
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][ConfigAsync] End - Total Duration: {stopwatch.ElapsedMilliseconds}ms (no Proxy creation), SessionId: {Id}");
    }

    public override Task<string> GetDescriptionAsync()
    {
        throw new NotImplementedException();
    }

    public async Task InitAsync(Guid ChatManagerGuid)
    {
        Logger.LogInformation($"[GodChatGAgent][InitAsync] Start - SessionId: {Id}, ChatManagerGuid: {ChatManagerGuid}");
        
        RaiseEvent(new SetChatManagerGuidEvent
        {
            ChatManagerGuid = ChatManagerGuid.ToString()
        });
        
        // CRITICAL: Confirm events to persist the ChatManagerGuid before returning
        await ConfirmEventsAsync();

        Logger.LogInformation($"[GodChatGAgent][InitAsync] End - SessionId: {Id}, State.ChatManagerGuid: {State.ChatManagerGuid}");
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
    
    public Task<ChatMessageListProto> GetChatMessageAsync()
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][GetSessionMessageListAsync] - session:ID {Id.ToString()} ,message={JsonConvert.SerializeObject(State.ChatHistory)}");
        return Task.FromResult(State.ChatHistory.ToChatMessageListProto());
    }

    public Task<ChatMessageWithMetaListProto> GetChatMessageWithMetaAsync()
    {
        Logger.LogDebug(
            $"[GodChatGAgent][GetChatMessageWithMetaAsync] - sessionId: {Id}, messageCount: {State.ChatHistory.Count}, metaCount: {State.ChatMessageMetas.Count}");

        var result = new ChatMessageWithMetaListProto();

        // Combine ChatHistory with ChatMessageMetas (reusing existing Proto types)
        for (int i = 0; i < State.ChatHistory.Count; i++)
        {
            var msgProto = State.ChatHistory[i];
            var metaProto = i < State.ChatMessageMetas.Count 
                ? State.ChatMessageMetas[i] 
                : new Aevatar.Agents.GodGPT.Protos.GodChat.ChatMessageMetaProto();

            result.Entries.Add(new ChatMessageWithMetaEntryProto
            {
                Message = msgProto,
                Meta = metaProto
            });
        }

        Logger.LogDebug(
            $"[GodChatGAgent][GetChatMessageWithMetaAsync] - sessionId: {Id}, returned {result.Entries.Count} messages with metadata");

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
    
    #region Helper Methods
    
    /// <summary>
    /// Build PromptWithStreamInputProto for RPC call to AIAgentStatusProxy
    /// </summary>
    private PromptWithStreamInputProto BuildPromptWithStreamInputProto(
        string prompt,
        List<ChatMessage>? history,
        ExecutionPromptSettings? promptSettings,
        AIChatContextDto? context,
        List<string>? imageKeys)
    {
        var input = new PromptWithStreamInputProto { Prompt = prompt };
        
        // Convert history
        if (history != null)
        {
            foreach (var msg in history)
            {
                input.History.Add(new Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos.ChatMessageProto
                {
                    Role = msg.Role ?? "",
                    Content = msg.Content ?? "",
                    TimestampTicks = msg.Timestamp.Ticks,
                    ChatRole = (int)msg.ChatRole
                });
            }
        }
        
        // Convert prompt settings
        if (promptSettings != null)
        {
            input.PromptSettings = new Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos.ExecutionPromptSettingsProto();
            if (promptSettings.Temperature != null) input.PromptSettings.Temperature = promptSettings.Temperature;
            if (promptSettings.MaxTokens != null) input.PromptSettings.MaxTokens = promptSettings.MaxTokens;
            if (promptSettings.TopP != null) input.PromptSettings.TopP = promptSettings.TopP;
            if (promptSettings.FrequencyPenalty != null) input.PromptSettings.FrequencyPenalty = promptSettings.FrequencyPenalty;
            if (promptSettings.PresencePenalty != null) input.PromptSettings.PresencePenalty = promptSettings.PresencePenalty;
            if (promptSettings.StopSequences != null) input.PromptSettings.StopSequences.AddRange(promptSettings.StopSequences);
            if (promptSettings.Model != null) input.PromptSettings.Model = promptSettings.Model;
        }
        
        // Convert context
        if (context != null)
        {
            input.Context = new AIChatContextProto
            {
                RequestId = context.RequestId.ToString()
            };
            if (context.AgentId != null) input.Context.AgentId = context.AgentId;
            if (context.SessionId != null) input.Context.SessionId = context.SessionId;
            if (context.UserId != null) input.Context.UserId = context.UserId;
            if (context.SystemPrompt != null) input.Context.SystemPrompt = context.SystemPrompt;
            if (context.ChatId != null) input.Context.ChatId = context.ChatId;
            if (context.MessageId != null) input.Context.MessageId = context.MessageId;
            if (context.Metadata != null)
            {
                foreach (var kvp in context.Metadata)
                {
                    input.Context.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }
        
        // Add image keys
        if (imageKeys != null)
        {
            input.ImageKeys.AddRange(imageKeys);
        }
        
        return input;
    }
    
    /// <summary>
    /// Build ChatWithHistoryInputProto for RPC call to AIAgentStatusProxy
    /// </summary>
    private ChatWithHistoryInputProto BuildChatWithHistoryInputProto(
        string prompt,
        List<ChatMessage>? history,
        ExecutionPromptSettings? promptSettings,
        AIChatContextDto? context)
    {
        var input = new ChatWithHistoryInputProto { Prompt = prompt };
        
        // Convert history
        if (history != null)
        {
            foreach (var msg in history)
            {
                input.History.Add(new Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos.ChatMessageProto
                {
                    Role = msg.Role ?? "",
                    Content = msg.Content ?? "",
                    TimestampTicks = msg.Timestamp.Ticks,
                    ChatRole = (int)msg.ChatRole
                });
            }
        }
        
        // Convert prompt settings
        if (promptSettings != null)
        {
            input.PromptSettings = new Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos.ExecutionPromptSettingsProto();
            if (promptSettings.Temperature != null) input.PromptSettings.Temperature = promptSettings.Temperature;
            if (promptSettings.MaxTokens != null) input.PromptSettings.MaxTokens = promptSettings.MaxTokens;
            if (promptSettings.TopP != null) input.PromptSettings.TopP = promptSettings.TopP;
            if (promptSettings.FrequencyPenalty != null) input.PromptSettings.FrequencyPenalty = promptSettings.FrequencyPenalty;
            if (promptSettings.PresencePenalty != null) input.PromptSettings.PresencePenalty = promptSettings.PresencePenalty;
            if (promptSettings.StopSequences != null) input.PromptSettings.StopSequences.AddRange(promptSettings.StopSequences);
            if (promptSettings.Model != null) input.PromptSettings.Model = promptSettings.Model;
        }
        
        // Convert context
        if (context != null)
        {
            input.Context = new AIChatContextProto
            {
                RequestId = context.RequestId.ToString()
            };
            if (context.AgentId != null) input.Context.AgentId = context.AgentId;
            if (context.SessionId != null) input.Context.SessionId = context.SessionId;
            if (context.UserId != null) input.Context.UserId = context.UserId;
            if (context.SystemPrompt != null) input.Context.SystemPrompt = context.SystemPrompt;
            if (context.ChatId != null) input.Context.ChatId = context.ChatId;
            if (context.MessageId != null) input.Context.MessageId = context.MessageId;
            if (context.Metadata != null)
            {
                foreach (var kvp in context.Metadata)
                {
                    input.Context.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }
        
        return input;
    }
    
    /// <summary>
    /// Convert ChatWithHistoryResultProto to ChatMessageListProto
    /// </summary>
    private ChatMessageListProto ConvertToChatMessageListProto(ChatWithHistoryResultProto? result)
    {
        var listProto = new ChatMessageListProto();
        if (result?.Messages != null)
        {
            foreach (var msg in result.Messages)
            {
                listProto.Messages.Add(new Aevatar.Agents.GodGPT.Protos.GodChat.ChatMessageProto
                {
                    ChatRole = msg.ChatRole,  // Both are int32
                    Content = msg.Content ?? ""
                });
            }
        }
        return listProto;
    }
    
    #endregion
}
