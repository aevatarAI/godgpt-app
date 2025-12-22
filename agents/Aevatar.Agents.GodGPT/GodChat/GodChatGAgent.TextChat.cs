using System.Diagnostics;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.Common.Constants;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Text chat related methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    public async Task StartStreamChatAsync(StartStreamChatInput input)
    {
        Guid sessionId = input.SessionId;
        string sysmLLM = input.SysmLLM; 
        string content = input.Content;
        string chatId = input.ChatId;
        ExecutionPromptSettings promptSettings = input.PromptSettings;
        bool isHttpRequest = input.IsHttpRequest;
        string? region = input.region;
        List<string>? images = input.images;
        
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[GodChatGAgent][StartStreamChatAsync] {sessionId.ToString()} start. region:{region}");

        // Get language from RequestContext with error handling
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        Logger.LogDebug($"[GodChatGAgent][StartStreamChatAsync] Language from context: {language}");

        var actionType = images == null || images.IsNullOrEmpty()
            ? QuotaActionType.Conversation
            : QuotaActionType.ImageConversation;
        
        var userQuotaGAgent = await GetUserQuotaAgentAsync(Guid.Parse(State.ChatManagerGuid));
        var actionResultDto =
            await userQuotaGAgent.ExecuteActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString(), actionType);
        if (!actionResultDto.Success)
        {
            Logger.LogDebug($"[GodChatGAgent][StartStreamChatAsync] {sessionId.ToString()} Access restricted");

            //save conversation data
            await SetSessionTitleAsync(sessionId, content);
            var chatMessages = new List<ChatMessage>();
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = content,
                ImageKeys = images
            });
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.Assistant,
                Content = actionResultDto.Message
            });
            RaiseEvent(new AddChatMessagesEvent
            {
                Messages = { chatMessages.ToProtoList() }
            });
            
            RaiseEvent(new AddChatMessageMetasEvent
            {
                ChatMessageMetas = { }
            });
            
            await ConfirmEventsAsync();

            //2. Directly respond with error information.
            var chatMessage = new ResponseStreamGodChat()
            {
                Response = actionResultDto.Message,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(chatMessage);
            }
            else
            {
                await PublishAsync(chatMessage.ToProto());
            }

            return;
        }

        Logger.LogDebug($"[GodChatGAgent][StartStreamChatAsync] {sessionId.ToString()} - Validation passed");
        
        await SetSessionTitleAsync(sessionId, content);
        var configuration = await GetConfigurationAsync();
        await GodStreamChatAsync(sessionId, configuration.GetSystemLLM(),
            configuration.GetStreamingModeEnabled(),
            content, chatId, promptSettings, isHttpRequest, region, images: images, 
            userLocalTime: input.UserLocalTime, userTimeZoneId: input.UserTimeZoneId);
        
        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][StartStreamChatAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
    }

    public async Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null, 
        List<string>? images = null)
    {
        Logger.LogDebug($"[GodChatGAgent][StreamChatWithSessionAsync] start: {sessionId.ToString()}");
        await StartStreamChatAsync(new StartStreamChatInput
        {
            SessionId = sessionId,
            SysmLLM = sysmLLM,
            Content = content,
            ChatId = chatId,
            PromptSettings = promptSettings,
            IsHttpRequest = isHttpRequest,
            region = region,
            images = images
        });
    }

    public async Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, bool addToHistory = true, List<string>? images = null, DateTime? userLocalTime = null,
        string? userTimeZoneId = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug(
            $"[GodChatGAgent][GodStreamChatAsync] agent start  session {sessionId.ToString()}, chat {chatId}, region {region}");
        
        var aiChatContextDto =
            CreateAIChatContext(sessionId, llm, streamingModeEnabled, message, chatId, promptSettings, isHttpRequest,
                region, images);

        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);

        if (aiAgentStatusProxy != null)
        {
            var proxyId = aiAgentStatusProxy.Id;
            // Ensure proxy is initialized before proceeding
            await EnsureProxyInitializedAsync(proxyId, sessionId);
            
            Logger.LogDebug(
                $"[GodChatGAgent][GodStreamChatAsync] agent {aiAgentStatusProxy.Id.ToString()}, session {sessionId.ToString()}, chat {chatId}");

            // Check if this is a voice chat from context
            bool isPromptVoiceChat = false;
            if (aiChatContextDto.MessageId != null)
            {
                try
                {
                    var messageData =
                        JsonConvert.DeserializeObject<Dictionary<string, object>>(aiChatContextDto.MessageId);
                    isPromptVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "[GodChatGAgent][GodStreamChatAsync] Failed to parse MessageId for voice chat detection");
                }
            }

            // User message without additional prefixes (timestamp now in system prompt)
            string enhancedMessage = message;
            if (!isPromptVoiceChat)
            {
                var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} Language from context: {language}");
                var homeDosAndDontPromptMessage = _localizationService.GetLocalizedMessage(ExceptionMessageKeys.HomeDosAndDontPrompt,language);
                var chatPageMessageAfterSync = _localizationService.GetLocalizedMessage(ExceptionMessageKeys.ChatPageMessageAfterSync,language);
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} homeDosAndDontPromptMessage: {homeDosAndDontPromptMessage}");
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} message: {message}, {message == homeDosAndDontPromptMessage}");
                var isDailyGuide = State.PromptTemplate == DailyGuide;
                if (isDailyGuide && (message.StartsWith(homeDosAndDontPromptMessage) || message.StartsWith(chatPageMessageAfterSync)))
                {
                    enhancedMessage = await GenerateDailyRecommendationsAsync(language, userLocalTime, userTimeZoneId);
                    Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} enhancedMessage: {enhancedMessage}");
                }
                
                enhancedMessage = enhancedMessage + ChatPrompts.ConversationSuggestionsPrompt;
                Logger.LogDebug(
                    $"[GodChatGAgent][GodStreamChatAsync] {sessionId} Added conversation suggestions prompt for text chat");
            }

            var settings = promptSettings ?? new ExecutionPromptSettings();
            settings.Temperature = "1.0";
            var result = await aiAgentStatusProxy.PromptWithStreamAsync(enhancedMessage, State.ChatHistory.FromProtoList(), settings,
                context: aiChatContextDto, imageKeys: images);
            if (!result)
            {
                Logger.LogError($"Failed to initiate streaming response. {Id.ToString()}");
            }

            if (addToHistory)
            {
                var historyStopwatch = Stopwatch.StartNew();
                // Optimize: Use combined event to reduce RaiseEvent calls from 3 to 1
                RaiseEvent(new StreamChatCombinedEvent
                {
                    ChatList = 
                    {
                        new ChatMessage
                        {
                            ChatRole = ChatRole.User,
                            Content = message,
                            ImageKeys = images
                        }.ToProto()
                    },
                    ChatTime = Timestamp.FromDateTime(DateTime.UtcNow),
                    ChatMessageMetas = { }
                });

                historyStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] Combined RaiseEvent - Duration: {historyStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
            }
        }
        else
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodStreamChatAsync] history agent, session {sessionId.ToString()}, chat {chatId}");
        }

        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        return string.Empty;
    }

    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content,
        ExecutionPromptSettings promptSettings = null)
    {
        var title = "";
        if (State.Title.IsNullOrEmpty())
        {
            // Take first 4 words and limit total length to 100 characters
            title = string.Join(" ", content.Split(" ").Take(4));
            if (title.Length > 100)
            {
                title = title.Substring(0, 100);
            }

            RaiseEvent(new Aevatar.Agents.GodGPT.Protos.GodChat.RenameChatTitleEvent()
            {
                Title = title
            });

            await ConfirmEventsAsync();

            var chatManagerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(Guid.Parse(State.ChatManagerGuid));
            var chatManagerGAgent = (IChatManagerGAgent)chatManagerActor.GetAgent();
            await chatManagerGAgent.RenameChatTitleAsync(new RenameChatTitleEvent()
            {
                SessionId = sessionId,
                Title = title
            });
        }

        var configuration = await GetConfigurationAsync();
        var response = await GodChatAsync(configuration.GetSystemLLM(), content, promptSettings);
        return new Tuple<string, string>(response, title);
    }

    public async Task<List<ChatMessage>?> ChatWithHistory(Guid sessionId, string systemLLM, string content,
        string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null)
    {
        Logger.LogDebug($"[GodChatGAgent][ChatWithHistory] {sessionId.ToString()} content:{content} start.");
        var sw = new Stopwatch();
        sw.Start();
        var history = State.ChatHistory;
        if (history.IsNullOrEmpty())
        {
            return new List<ChatMessage>();
        }

        var configuration = await GetConfigurationAsync();
        var llm = configuration.GetSystemLLM();
        var streamingModeEnabled = configuration.GetStreamingModeEnabled();

        var aiAgentStatusProxy = await GetInitializedProxyAsync(region, sessionId);
        if (aiAgentStatusProxy == null)
        {
            Logger.LogError($"[GodChatGAgent][ChatWithHistory] No AIGAgent available. {sessionId.ToString()}");
            return new List<ChatMessage>();
        }

        var settings = promptSettings ?? new ExecutionPromptSettings();
        settings.Temperature = "1.0";

        var aiChatContextDto = CreateAIChatContext(sessionId, llm, streamingModeEnabled, content, chatId,
            promptSettings, isHttpRequest, region);
        var response = await aiAgentStatusProxy.ChatWithHistory(content, State.ChatHistory.FromProtoList(), settings, aiChatContextDto);
        sw.Stop();
        Logger.LogDebug(
            $"[GodChatGAgent][ChatWithHistory] {sessionId.ToString()}, response:{JsonConvert.SerializeObject(response)} - step4,time use:{sw.ElapsedMilliseconds}");
        return response;
    }
    
    public async Task<List<ChatMessage>?> ChatWithoutHistoryAsync(Guid sessionId, string systemLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null)
    {
        Logger.LogDebug($"[GodChatGAgent][ChatWithUserId] {sessionId.ToString()} content:{content} start.");
        var sw = new Stopwatch();
        sw.Start();

        var configuration = await GetConfigurationAsync();
        var llm = configuration.GetSystemLLM();
        var streamingModeEnabled = configuration.GetStreamingModeEnabled();
        
        var aiAgentStatusProxy = await GetInitializedProxyAsync(region, sessionId);
        if (aiAgentStatusProxy == null)
        {
            Logger.LogError($"[GodChatGAgent][ChatWithHistory] No AIGAgent available. {sessionId.ToString()}");
            return new List<ChatMessage>();
        }

        var settings = promptSettings ?? new ExecutionPromptSettings();
        settings.Temperature = "1.0";
        
        var aiChatContextDto = CreateAIChatContext(sessionId, llm, streamingModeEnabled, content, chatId, promptSettings, isHttpRequest, region);
        var response = await aiAgentStatusProxy.ChatWithHistory(content,  State.ChatHistory.FromProtoList(), settings, aiChatContextDto);
        sw.Stop();
        Logger.LogDebug($"[GodChatGAgent][ChatWithUserId] {sessionId.ToString()}, response:{JsonConvert.SerializeObject(response)} - step4,time use:{sw.ElapsedMilliseconds}");
        return response;
    }

    public async Task<string> GodChatAsync(string llm, string message,
        ExecutionPromptSettings? promptSettings = null)
    {
        throw new Exception("The method has expired");
    }
}

