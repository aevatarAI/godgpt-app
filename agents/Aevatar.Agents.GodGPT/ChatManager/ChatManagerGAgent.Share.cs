using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    public async Task<Guid> GenerateChatShareContentAsync(Guid sessionId)
    {
        var methodStart = System.Diagnostics.Stopwatch.StartNew();
        Logger.LogInformation("[PERF][ChatGAgentManager] GenerateChatShareContentAsync START - SessionId: {SessionId}", sessionId);
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        if (State.CurrentShareCount >= State.MaxShareCount)
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, Exceed the maximum sharing limit. {State.CurrentShareCount}");
            var parameters = new Dictionary<string, string>
            {
                ["MaxShareCount"] = State.MaxShareCount.ToString()
            };
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.SharesReached, language, parameters);

            throw new UserFriendlyException(localizedMessage);
        }

        // Step 1: Get session messages
        var step1Start = methodStart.ElapsedMilliseconds;
        var chatMessagesProto = await GetSessionMessageListAsync(sessionId);
        var step1End = methodStart.ElapsedMilliseconds;
        Logger.LogInformation("[PERF][Share] Step1_GetMessages: {Ms}ms, MessageCount: {Count}", 
            step1End - step1Start, chatMessagesProto?.Messages.Count ?? 0);
        
        if (chatMessagesProto == null || chatMessagesProto.Messages.Count == 0)
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, chatMessages is null");
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidSession, language);
            throw new UserFriendlyException(localizedMessage);
        }

        var shareId = Guid.NewGuid();
        
        // Step 2: Create ShareLinkGAgent Actor
        var step2Start = methodStart.ElapsedMilliseconds;
        var shareLinkActor = await _actorFactory.CreateGAgentActorAsync<ShareLinkGAgent>(shareId.ToString());
        var step2End = methodStart.ElapsedMilliseconds;
        Logger.LogInformation("[PERF][Share] Step2_CreateActor: {Ms}ms", step2End - step2Start);
        
        var shareLink = shareLinkActor.As<IShareLinkGAgent>();
        
        // Build ShareLinkProto directly
        var shareLinkProto = new ShareLinkProto
        {
            UserId = Id,
            SessionId = sessionId.ToString(),
            CreateTime = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        shareLinkProto.Messages.AddRange(chatMessagesProto.Messages);
        
        // Step 3: Save share content via RPC
        var step3Start = methodStart.ElapsedMilliseconds;
        await shareLink.SaveShareContentAsync(shareLinkProto);
        var step3End = methodStart.ElapsedMilliseconds;
        Logger.LogInformation("[PERF][Share] Step3_SaveContent: {Ms}ms, ProtoSize: {Size}bytes", 
            step3End - step3Start, shareLinkProto.CalculateSize());
        
        // Log state before raising event
        var sessionBeforeEvent = State.GetSession(sessionId);
        var shareIdsBefore = sessionBeforeEvent?.GetShareIds() ?? new List<Guid>();
        
        RaiseEvent(new GenerateChatShareContentEvent
        {
            SessionId = sessionId.ToString(),
            ShareId = shareId.ToString()
        });

        // Step 4: Confirm events (EventSourcing persistence)
        var step4Start = methodStart.ElapsedMilliseconds;
        await ConfirmEventsAsync();
        var step4End = methodStart.ElapsedMilliseconds;
        Logger.LogInformation("[PERF][Share] Step4_ConfirmEvents: {Ms}ms", step4End - step4Start);
        
        // Log state after confirming event
        var sessionAfterEvent = State.GetSession(sessionId);
        var shareIdsAfter = sessionAfterEvent?.GetShareIds() ?? new List<Guid>();
        Logger.LogInformation(
            "[ChatGAgentManager][GenerateChatShareContentAsync] State AFTER ConfirmEvents - SessionExists: {Exists}, ShareIdCount: {Count}, NewShareId: {Expected}",
            sessionAfterEvent != null, shareIdsAfter.Count, shareId);
        
        // Verify the shareId was actually saved - FAIL if not saved correctly
        if (!shareIdsAfter.Contains(shareId))
        {
            Logger.LogError(
                "[ChatGAgentManager][GenerateChatShareContentAsync] CRITICAL - ShareId NOT SAVED! Expected: {Expected}, StoredShareIds: [{Actual}]",
                shareId, string.Join(", ", shareIdsAfter));
            
            // Don't return invalid shareId - throw exception so client knows share failed
            throw new InvalidOperationException(
                $"Share link creation failed: event was not persisted correctly. Expected ShareId {shareId} not found in stored list.");
        }
        
        Logger.LogInformation(
            "[PERF][Share] TOTAL: {TotalMs}ms - Step1={Step1}ms, Step2={Step2}ms, Step3={Step3}ms, Step4={Step4}ms - SessionId: {SessionId}, ShareId: {ShareId}",
            methodStart.ElapsedMilliseconds, 
            step1End - step1Start, step2End - step2Start, step3End - step3Start, step4End - step4Start,
            sessionId, shareId);
        
        return shareId;
    }

    public async Task<ShareLinkProto> GetChatShareContentAsync(Guid sessionId, Guid shareId)
    {
        // Enhanced diagnostic logging for share link issues
        Logger.LogInformation(
            "[ChatGAgentManager][GetChatShareContentAsync] START - UserId: {UserId}, SessionId: {SessionId}, ShareId: {ShareId}, " +
            "State.SessionCount: {SessionCount}, State.CurrentShareCount: {CurrentShareCount}",
            Id, sessionId, shareId, State.SessionInfoList.Count, State.CurrentShareCount);
        
        // Log all sessions in state for debugging
        foreach (var s in State.SessionInfoList.Take(10))
        {
            var sShareIds = s.GetShareIds();
            Logger.LogInformation(
                "[ChatGAgentManager][GetChatShareContentAsync] Session in State: SessionId={SId}, Title={Title}, ShareIdCount={ShareIdCount}",
                s.SessionId, s.Title ?? "(empty)", sShareIds.Count);
        }
        if (State.SessionInfoList.Count > 10)
        {
            Logger.LogInformation("[ChatGAgentManager][GetChatShareContentAsync] ... and {More} more sessions", 
                State.SessionInfoList.Count - 10);
        }
        
        var sessionInfo = State.GetSession(sessionId);
        Logger.LogInformation(
            "[ChatGAgentManager][GetChatShareContentAsync] GetSession result: Found={Found}, SessionId={FoundSessionId}",
            sessionInfo != null, sessionInfo?.SessionId ?? "(null)");
        
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        if (sessionInfo == null)
        {
            Logger.LogWarning(
                "[ChatGAgentManager][GetChatShareContentAsync] FAIL - Session NOT FOUND in State. " +
                "RequestedSessionId: {SessionId}, AvailableSessionIds: [{AvailableIds}]",
                sessionId, string.Join(", ", State.SessionInfoList.Select(s => s.SessionId).Take(5)));
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.ConversationDeleted, language);
            throw new UserFriendlyException(localizedMessage);
        }

        var shareIds = sessionInfo.GetShareIds();
        Logger.LogInformation(
            "[ChatGAgentManager][GetChatShareContentAsync] ShareId check - StoredShareIds: [{StoredShareIds}], " +
            "RequestedShareId: {RequestedShareId}, Match: {Match}",
            string.Join(", ", shareIds), shareId, shareIds.Contains(shareId));
        
        if (shareIds.IsNullOrEmpty() || !shareIds.Contains(shareId))
        {
            Logger.LogWarning(
                "[ChatGAgentManager][GetChatShareContentAsync] FAIL - ShareId NOT FOUND. " +
                "StoredShareIds: [{StoredShareIds}], RequestedShareId: {RequestedShareId}",
                string.Join(", ", shareIds), shareId);
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.ConversationDeleted, language);
            throw new UserFriendlyException(localizedMessage);
        }

        // Use new ShareLinkGAgent instead of Orleans Grain
        var shareLinkActor = await _actorFactory.CreateGAgentActorAsync<ShareLinkGAgent>(shareId.ToString());
        var shareLink = shareLinkActor.As<IShareLinkGAgent>();
        
        return await shareLink.GetShareContentAsync();
    }
}
