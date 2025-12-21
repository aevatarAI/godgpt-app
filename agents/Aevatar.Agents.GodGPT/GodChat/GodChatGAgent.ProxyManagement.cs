using System.Diagnostics;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.GEvents;
using GodGPT.GAgents.Common.Constants;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Proxy management related methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    #region Proxy Status Updates
    
    [EventHandler]
    public async Task HandleEventAsync(UpdateProxyInitStatusGEvent @event)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[GodChatGAgent][HandleEventAsync][UpdateProxyInitStatusGEvent] Start - SessionId: {Id}, ProxyId: {@event.ProxyId}, Status: {@event.Status}");
        
        // Update the proxy initialization status using Protobuf event
        RaiseEvent(new UpdateProxyInitStatusEvent
        {
            ProxyId = @event.ProxyId.ToString(),
            Status = @event.Status.ToProto()
        });
        await ConfirmEventsAsync();
        
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][HandleEventAsync][UpdateProxyInitStatusGEvent] End - Duration: {stopwatch.ElapsedMilliseconds}ms, Status updated to: {@event.Status} for proxy: {@event.ProxyId}");
    }
    
    /// <summary>
    /// Update proxy initialization status - public interface method called by AIAgentStatusProxy
    /// </summary>
    public async Task UpdateProxyInitStatusAsync(Guid proxyId, ProxyInitStatus status)
    {
        // Using old C# class UpdateProxyInitStatusGEvent where ProxyId is Guid
        await HandleEventAsync(new UpdateProxyInitStatusGEvent
        {
            ProxyId = proxyId,
            Status = status
        });
    }
    
    #endregion

    #region Proxy Retrieval
    
    private async Task<IAIAgentStatusProxy?> GetProxyByRegionAsync(string? region)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var isCN = GodGPTLanguageHelper.CheckClientIsCNFromContext();
        if (string.IsNullOrWhiteSpace(region))
        {
            region = isCN ? CNDefaultRegion : DefaultRegion;
        }
        else
        {
            if (region.Equals(ConsoleRegion) && isCN)
            {
                region = CNConsoleRegion;
            }
        }
        Logger.LogDebug(
            $"[GodChatGAgent][GetProxyByRegionAsync] session {Id.ToString()},isCN:{isCN}, Region: {region}");

        var existingProxy = State.RegionProxies?.FirstOrDefault(r => r.Region == region);
        var proxyIds = existingProxy?.ProxyIds?.Select(Guid.Parse).ToList();
        
        if (proxyIds == null || !proxyIds.Any())
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GetProxyByRegionAsync] session {Id.ToString()}, No proxies found for region {region}, initializing.");
            
            var initStopwatch = Stopwatch.StartNew();
            proxyIds = await InitializeRegionProxiesAsync(region);
            initStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] InitializeRegionProxiesAsync - Duration: {initStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}, Region: {region}");
            
            var eventStopwatch = Stopwatch.StartNew();
            RaiseEvent(new UpdateRegionProxiesEvent
            {
                RegionProxies = 
                {
                    new RegionProxiesEntryProto
                    {
                        Region = region,
                        ProxyIds = { proxyIds.Select(g => g.ToString()) }
                    }
                }
            });
            await ConfirmEventsAsync();
            eventStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] UpdateRegionProxiesEvent - Duration: {eventStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}");
        }

        foreach (var proxyId in proxyIds)
        {
            var proxy = _clusterClient.GetGrain<IAIAgentStatusProxy>(proxyId);
            if (await proxy.IsAvailableAsync())
            {
                totalStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}");
                return proxy;
            }
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] ProxyCheck_Failed -, ProxyId: {proxyId}, SessionId: {Id}");
        }

        Logger.LogDebug(
            $"[GodChatGAgent][GetProxyByRegionAsync] session {Id.ToString()}, No proxies initialized for region {region}");
        if (region == DefaultRegion || region == CNDefaultRegion)
        {
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] TOTAL_Time (no proxies) - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}");
            return null;
        }

        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] Recursive call to DefaultRegion - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}");
        if (isCN)
        {
            return await GetProxyByRegionAsync(CNDefaultRegion);
        }
        return await GetProxyByRegionAsync(DefaultRegion);
    }

    private async Task<List<Guid>> InitializeRegionProxiesAsync(string region, string rolePrompts = "")
    {
        var stopwatch = Stopwatch.StartNew();
        var llmsForRegion = GetLLMsForRegion(region);
        if (llmsForRegion.IsNullOrEmpty())
        {
            stopwatch.Stop();
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {Id.ToString()}, initialized proxy for region {region}, LLM not config Duration: {stopwatch.ElapsedMilliseconds}ms");
            return new List<Guid>();
        }
        
        var oldSystemPrompt = (await GetConfigurationAsync()).GetPrompt();

        var proxies = new List<Guid>();
        var totalProxyStopwatch = Stopwatch.StartNew();
        foreach (var llm in llmsForRegion)
        {
            var systemPrompt = rolePrompts.IsNullOrWhiteSpace() ? State.PromptTemplate : rolePrompts;
            
            // Add conversation suggestions prompt and timestamp to system prompt
            var dateInfo = $"Today's date is: {DateTime.UtcNow:yyyy-MM-dd}. Please use this date as reference for time-related questions.";
            
            var isDailyGuide = systemPrompt == DailyGuide;
            if (isDailyGuide)
            {
                systemPrompt = @"Generate a personalized ""Today's Dos and Don'ts"" for the user based on their information and cosmological theories.";
            }
            else
            {
                if (llm != LocalBackupModel)
                {
                    systemPrompt = $"{systemPrompt}\n\n{ChatPrompts.ConversationSuggestionsPrompt}\n\n{dateInfo}";
                }
                else
                {
                    systemPrompt = $"{oldSystemPrompt} {systemPrompt}\n\n{ChatPrompts.ConversationSuggestionsPrompt}\n\n{dateInfo}";
                }
            }
            
            var proxy = _clusterClient.GetGrain<IAIAgentStatusProxy>(Guid.NewGuid());
            
            // TODO: [P2P_STREAM] Currently using direct grain call because new framework doesn't support P2P stream yet.
            _ = proxy.ConfigAsync(new AIAgentStatusProxyConfig
            {
                Instructions = systemPrompt,
                LLMConfig = new LLMConfigDto { SystemLLM = llm },
                StreamingModeEnabled = true,
                StreamingConfig = new StreamingConfig { BufferingSize = 32 },
                RequestRecoveryDelay = RequestRecoveryDelay,
                ParentId = Id
            }); // Fire and forget - don't await
            RaiseEvent(new UpdateProxyInitStatusEvent
            {
                ProxyId = proxy.GetPrimaryKey().ToString(),
                Status = ProxyInitStatus.Initializing.ToProto()
            });
            await ConfirmEventsAsync();
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {Id.ToString()}, UpdateProxyInitStatusEvent status Initializing proxyId {proxy.GetPrimaryKey().ToString()}");
            proxies.Add(proxy.GetPrimaryKey());
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {Id.ToString()}, initialized proxy for region {region} with LLM {llm}. id {proxy.GetPrimaryKey().ToString()}");
        }
        totalProxyStopwatch.Stop();
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][InitializeRegionProxiesAsync] End - Total Duration: {stopwatch.ElapsedMilliseconds}ms, ProxyCount: {proxies.Count}, TotalProxyTime: {totalProxyStopwatch.ElapsedMilliseconds}ms");
        return proxies;
    }
    
    private List<string> GetLLMsForRegion(string region)
    {
        var regionToLLMsMap = _llmRegionOptions.CurrentValue.RegionToLLMsMap;
        return regionToLLMsMap.TryGetValue(region, out var llms) ? llms : new List<string>();
    }

    /// <summary>
    /// Ensures that the specified proxy is initialized before proceeding with operations.
    /// This method implements retry logic with exponential backoff to wait for proxy initialization.
    /// </summary>
    private async Task EnsureProxyInitializedAsync(Guid proxyId, Guid sessionId)
    {
        const int maxRetries = 10;
        const int retryDelayMs = 200;
        var retryCount = 0;
        ProxyInitStatus proxyInitStatus = ProxyInitStatus.NotInitialized;
        
        while (retryCount < maxRetries)
        {
            var statusEntry = State.ProxyInitStatuses?.FirstOrDefault(s => s.ProxyId == proxyId.ToString());
            if (State.ProxyInitStatuses.IsNullOrEmpty() || statusEntry == null)
            {
                Logger.LogDebug($"[GodChatGAgent][EnsureProxyInitializedAsync] Historical data detected based on FirstChatTime, skipping proxy initialization check - ProxyId: {proxyId}, SessionId: {sessionId}");
                break;
            }
            proxyInitStatus = statusEntry.Status.FromProto();

            if (proxyInitStatus != ProxyInitStatus.Initialized)
            {
                retryCount++;
                Logger.LogDebug($"[GodChatGAgent][EnsureProxyInitializedAsync] Proxy not initialized - ProxyId: {proxyId}, Status: {proxyInitStatus}, Retry: {retryCount}/{maxRetries}, SessionId: {sessionId}");
                
                if (retryCount >= maxRetries)
                {
                    throw new Exception($"proxy:{proxyId} Not initialized after {maxRetries} retries, status:{proxyInitStatus}");
                }
                
                await Task.Delay(retryDelayMs);
                continue;
            }
            
            // Proxy is initialized, break out of retry loop
            Logger.LogDebug($"[GodChatGAgent][EnsureProxyInitializedAsync] Proxy initialization check successful - ProxyId: {proxyId}, Status: {proxyInitStatus}, Retries: {retryCount}, SessionId: {sessionId}");
            break;
        }
    }

    /// <summary>
    /// Gets an initialized AI agent status proxy for the specified region.
    /// </summary>
    private async Task<IAIAgentStatusProxy?> GetInitializedProxyAsync(string? region, Guid sessionId)
    {
        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);
        
        if (aiAgentStatusProxy != null)
        {
            var proxyId = aiAgentStatusProxy.GetPrimaryKey();
            await EnsureProxyInitializedAsync(proxyId, sessionId);
        }
        
        return aiAgentStatusProxy;
    }
    
    #endregion
}

