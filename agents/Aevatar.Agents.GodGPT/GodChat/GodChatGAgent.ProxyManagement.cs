using System.Diagnostics;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Common.Constants;
using GodGPT.GAgents.Common.Constants;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Proxy management related methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    #region Proxy Status Updates
    
    /// <summary>
    /// Update proxy initialization status - public interface method called by AIAgentStatusProxy
    /// </summary>
    public async Task UpdateProxyInitStatusAsync(string proxyId, ProxyInitStatus status)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[GodChatGAgent][UpdateProxyInitStatusAsync] Start - SessionId: {Id}, ProxyId: {proxyId}, Status: {status}");
        
        // Update the proxy initialization status using Protobuf event
        RaiseEvent(new UpdateProxyInitStatusEvent
        {
            ProxyId = proxyId,
            Status = status.ToProto()
        });
        await ConfirmEventsAsync();
        
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][UpdateProxyInitStatusAsync] End - Duration: {stopwatch.ElapsedMilliseconds}ms, Status updated to: {status} for proxy: {proxyId}");
    }
    
    #endregion

    #region Proxy Retrieval
    
    /// <summary>
    /// Gets a proxy and its ID for the specified region.
    /// Returns (proxy, proxyId) tuple.
    /// </summary>
    private async Task<(IAIAgentStatusProxy? Proxy, string? ProxyId)> GetProxyByRegionAsync(string? region)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var isCN = GodGPTLanguageHelper.CheckClientIsCN(Context);
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
        var proxyIds = existingProxy?.ProxyIds?.ToList();
        
        // Lazy initialization: create proxies if not exist
        if (proxyIds == null || !proxyIds.Any())
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GetProxyByRegionAsync] session {Id.ToString()}, No proxies found for region {region}, initializing.");
            
            var initStopwatch = Stopwatch.StartNew();
            proxyIds = await InitializeRegionProxiesAsync(region);
            initStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] InitializeRegionProxiesAsync - Duration: {initStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}, Region: {region}");
            
            // Save proxy IDs to state
            RaiseEvent(new UpdateRegionProxiesEvent
            {
                RegionProxies = 
                {
                    new RegionProxiesEntryProto
                    {
                        Region = region,
                        ProxyIds = { proxyIds }
                    }
                }
            });
            await ConfirmEventsAsync();
        }

        // Find first available proxy
        foreach (var proxyId in proxyIds)
        {
            var proxyActor = await _actorFactory.CreateGAgentActorAsync<AIAgentStatusProxy>(proxyId);
            var proxy = proxyActor.As<IAIAgentStatusProxy>();
            
            // Ensure ParentId is set (for proxies restored from old state)
            await proxy.ConfigAsync(new AIAgentStatusProxyConfigProto
            {
                RequestRecoveryDelay = Duration.FromTimeSpan(RequestRecoveryDelay),
                ParentId = Id.ToString()
            });
            
            if (await proxy.IsAvailableAsync())
            {
                totalStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] Found available proxy - Duration: {totalStopwatch.ElapsedMilliseconds}ms, ProxyId: {proxyId}, SessionId: {Id}");
                return (proxy, proxyId);
            }
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] Proxy not available - ProxyId: {proxyId}, SessionId: {Id}");
        }

        Logger.LogDebug(
            $"[GodChatGAgent][GetProxyByRegionAsync] session {Id.ToString()}, No proxies initialized for region {region}");
        if (region == DefaultRegion || region == CNDefaultRegion)
        {
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] TOTAL_Time (no proxies) - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}");
            return (null, null);
        }

        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] Recursive call to DefaultRegion - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {Id}");
        if (isCN)
        {
            return await GetProxyByRegionAsync(CNDefaultRegion);
        }
        return await GetProxyByRegionAsync(DefaultRegion);
    }

    private async Task<List<string>> InitializeRegionProxiesAsync(string region, string rolePrompts = "")
    {
        var stopwatch = Stopwatch.StartNew();
        var llmsForRegion = GetLLMsForRegion(region);
        if (llmsForRegion.IsNullOrEmpty())
        {
            stopwatch.Stop();
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {Id.ToString()}, initialized proxy for region {region}, LLM not config Duration: {stopwatch.ElapsedMilliseconds}ms");
            return new List<string>();
        }
        
        var oldSystemPrompt = await (await GetConfigurationAsync()).GetPromptAsync();
        var baseSystemPrompt = rolePrompts.IsNullOrWhiteSpace() ? State.PromptTemplate : rolePrompts;
        var dateInfo = $"Today's date is: {DateTime.UtcNow:yyyy-MM-dd}. Please use this date as reference for time-related questions.";

        // ✅ 并行创建所有 Proxy（性能优化：从串行 N*T 变为并行 T）
        var totalProxyStopwatch = Stopwatch.StartNew();
        var proxyTasks = llmsForRegion.Select(async llm =>
        {
            // Build system prompt for this LLM
            string systemPrompt;
            var isDailyGuide = baseSystemPrompt == DailyGuide;
            if (isDailyGuide)
            {
                systemPrompt = @"Generate a personalized ""Today's Dos and Don'ts"" for the user based on their information and cosmological theories.";
            }
            else if (llm != LocalBackupModel)
            {
                systemPrompt = $"{baseSystemPrompt}\n\n{ChatPrompts.ConversationSuggestionsPrompt}\n\n{dateInfo}";
            }
            else
            {
                systemPrompt = $"{oldSystemPrompt} {baseSystemPrompt}\n\n{ChatPrompts.ConversationSuggestionsPrompt}\n\n{dateInfo}";
            }
            
            // Create and configure proxy in parallel
            var newProxyId = Guid.NewGuid().ToString();
            var proxyActor = await _actorFactory.CreateGAgentActorAsync<AIAgentStatusProxy>(newProxyId);
            var proxy = proxyActor.As<IAIAgentStatusProxy>();
            
            // Configure with Protobuf config - pass GodChat's ID as ParentId for callbacks
            await proxy.ConfigAsync(new AIAgentStatusProxyConfigProto
            {
                RequestRecoveryDelay = Duration.FromTimeSpan(RequestRecoveryDelay),
                ParentId = Id.ToString(),
                ProviderName = llm
            });
            
            // Set the prompt template
            await proxy.SetPromptTemplateAsync(systemPrompt);
            
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {Id.ToString()}, initialized proxy for region {region} with LLM {llm}. id {newProxyId}");
            
            return newProxyId;
        }).ToList();
        
        // 等待所有 Proxy 并行创建完成
        var proxyIds = await Task.WhenAll(proxyTasks);
        totalProxyStopwatch.Stop();
        
        // ✅ 批量记录事件（性能优化：从 N 次 ConfirmEvents 变为 1 次）
        // NOTE: 由于 Proxy 创建和配置是同步完成的（await ConfigAsync/SetPromptTemplateAsync），
        // 所以直接设置状态为 Initialized（而非 Initializing）
        foreach (var proxyId in proxyIds)
        {
            RaiseEvent(new UpdateProxyInitStatusEvent
            {
                ProxyId = proxyId,
                Status = ProxyInitStatus.Initialized.ToProto()  // ✅ 直接设置为 Initialized
            });
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {Id.ToString()}, UpdateProxyInitStatusEvent status Initialized proxyId {proxyId}");
        }
        await ConfirmEventsAsync(); // 一次性确认所有事件
        
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][InitializeRegionProxiesAsync] End - Total Duration: {stopwatch.ElapsedMilliseconds}ms, ProxyCount: {proxyIds.Length}, TotalProxyTime: {totalProxyStopwatch.ElapsedMilliseconds}ms (parallel)");
        return proxyIds.ToList();
    }
    
    private List<string> GetLLMsForRegion(string region)
    {
        var regionToLLMsMap = _llmRegionOptions.CurrentValue.RegionToLLMsMap;
        return regionToLLMsMap.TryGetValue(region, out var llms) ? llms : new List<string>();
    }
    
    #endregion
}

