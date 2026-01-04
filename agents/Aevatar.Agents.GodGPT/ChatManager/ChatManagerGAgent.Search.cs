using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.GodChat;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.Abstractions.Extensions;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    public async Task<SessionListProto> SearchSessionsAsync(string keyword, int maxResults = 1000)
    {
        Logger.LogDebug($"[ChatGAgentManager][SearchSessionsAsync] keyword: {keyword}, maxResults: {maxResults}");

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new SessionListProto();
        }

        // Use the complete keyword for matching (no word splitting)
        var searchKeyword = keyword.Trim().ToLowerInvariant();

        // Validate keyword length to prevent performance issues
        if (searchKeyword.Length > 200)
        {
            Logger.LogWarning(
                $"[ChatGAgentManager][SearchSessionsAsync] Keyword too long: {searchKeyword.Length} chars");
            return new SessionListProto();
        }

        var searchResults = new List<(SessionInfoProto proto, int matchScore)>();

        // Search through sessions (limit to most recent 1000 for performance)
        var sessionsToSearch = State.SessionInfoList
            .OrderByDescending(s => s.CreateAt)
            .Take(1000)
            .ToList();

        foreach (var sessionInfo in sessionsToSearch)
        {
            try
            {
                var titleLower = sessionInfo.Title?.ToLowerInvariant() ?? "";
                var matchScore = 0;
                var hasMatch = false;

                // Check title matching
                var titleMatchScore = 0;
                if (titleLower.Contains(searchKeyword))
                {
                    titleMatchScore = titleLower == searchKeyword ? 100 : 50; // Complete match gets higher score
                    hasMatch = true;
                }

                // Get chat content for content matching
                string contentPreview = "";
                try
                {
                    var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(sessionInfo.SessionId);
                    var godChat = godChatActor.As<IGodChat>();
                    var chatMessages = await godChat.GetChatMessageAsync();
                    contentPreview = ChatContentHelper.ExtractChatContent(chatMessages);
                }
                catch (Exception contentEx)
                {
                    Logger.LogWarning(contentEx,
                        $"[ChatGAgentManager][SearchSessionsAsync] Failed to extract content for session {sessionInfo.SessionId}");
                    contentPreview = ""; // Continue search without content matching
                }

                var contentLower = contentPreview.ToLowerInvariant();

                // Check content matching
                var contentMatchScore = 0;
                if (!string.IsNullOrEmpty(contentPreview) && contentLower.Contains(searchKeyword))
                {
                    contentMatchScore =
                        contentLower == searchKeyword ? 30 : 15; // Complete content match vs partial match
                    hasMatch = true;
                }

                if (hasMatch)
                {
                    matchScore = titleMatchScore * 2 + contentMatchScore; // Title matching gets higher priority

                    var createAt = sessionInfo.CreateAt ?? Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-365), DateTimeKind.Utc));

                    var proto = new SessionInfoProto
                    {
                        SessionId = sessionInfo.SessionId,
                        Title = sessionInfo.Title,
                        CreateAt = createAt,
                        Guider = sessionInfo.Guider ?? string.Empty
                    };
                    // Copy all ShareIds
                    if (sessionInfo.ShareIds.Count > 0)
                    {
                        proto.ShareIds.AddRange(sessionInfo.ShareIds);
                    }

                    searchResults.Add((proto, matchScore));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    $"[ChatGAgentManager][SearchSessionsAsync] Failed to process session {sessionInfo.SessionId}");
                // Continue processing other sessions
                continue;
            }
        }

        // Sort by match score (descending) then by creation time (descending)
        var sortedResults = searchResults
            .OrderByDescending(r => r.matchScore)
            .ThenByDescending(r => r.proto.CreateAt?.ToDateTime() ?? DateTime.MinValue)
            .Take(maxResults)
            .Select(r => r.proto)
            .ToList();

        var result = new SessionListProto();
        result.Sessions.AddRange(sortedResults);

        Logger.LogDebug(
            $"[ChatGAgentManager][SearchSessionsAsync] Found {result.Sessions.Count} matches for keyword: {keyword}");
        return result;
    }
}
