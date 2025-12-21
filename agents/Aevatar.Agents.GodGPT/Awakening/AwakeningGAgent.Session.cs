using Aevatar.GAgents.AI.Common;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.GodChat;
using GodGPT.GAgents.Awakening.Dtos;
using Microsoft.Extensions.Logging;

namespace GodGPT.GAgents.Awakening;

/// <summary>
/// Awakening GAgent - Session retrieval methods
/// </summary>
public partial class AwakeningGAgent
{
    #region Session Methods

    /// <summary>
    /// Get the user's latest non-empty session records
    /// </summary>
    public async Task<List<SessionContentDto>> GetLatestNonEmptySessionAsync()
    {
        try
        {
            // Get current user ID through Grain's Primary Key
            var userId = Id;
            
            // Get ChatManagerGAgent for this user using new framework
            var chatManagerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userId);
            var chatManager = (IChatManagerGAgent)chatManagerActor.GetAgent();
            var sessionList = await chatManager.GetSessionListAsync();
            
            if (sessionList == null || sessionList.Count == 0)
            {
                return new List<SessionContentDto>();
            }
            
            SessionContentDto? firstNonEmptySession = null;
            SessionContentDto? novaChimeSession = null;
            
            // Single pass: Find both sessions in one traversal with optimizations
            for (int i = sessionList.Count - 1; i >= 0; i--)
            {
                var session = sessionList[i];
                
                // Optimization: Skip sessions with empty titles as they likely have no messages
                if (string.IsNullOrEmpty(session.Title))
                {
                    continue;
                }
                
                var messages = await chatManager.GetSessionMessageListAsync(session.SessionId);
                
                if (messages != null && messages.Count > 0)
                {
                    var sessionContent = new SessionContentDto
                    {
                        SessionId = session.SessionId,
                        Title = session.Title ?? string.Empty,
                        Messages = messages,
                        LastActivityTime = session.CreateAt,
                        ExtractedContent = ExtractCoreContent(messages)
                    };
                    
                    // Check if this is the first non-empty session we found
                    if (firstNonEmptySession == null)
                    {
                        firstNonEmptySession = sessionContent;
                    }
                    
                    // Check if this is a Nova·Chime session
                    if (session.Guider == NovaChimeGuider && novaChimeSession == null)
                    {
                        novaChimeSession = sessionContent;
                    }
                    
                    // Early exit: If we've found both types, no need to continue
                    if (firstNonEmptySession != null && novaChimeSession != null)
                    {
                        break;
                    }
                }
            }
            
            // Return logic based on findings
            var result = new List<SessionContentDto>();
            
            if (firstNonEmptySession != null && novaChimeSession != null)
            {
                // If both found and they're the same session (Nova·Chime is the first non-empty)
                if (firstNonEmptySession.SessionId == novaChimeSession.SessionId)
                {
                    result.Add(firstNonEmptySession);
                }
                else
                {
                    // Different sessions: add regular first, Nova·Chime second
                    result.Add(firstNonEmptySession);
                    result.Add(novaChimeSession);
                }
            }
            else if (firstNonEmptySession != null)
            {
                // Only found regular non-empty session
                result.Add(firstNonEmptySession);
            }
            else if (novaChimeSession != null)
            {
                // Only found Nova·Chime session
                result.Add(novaChimeSession);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest non-empty sessions for user {UserId}", Id);
            return new List<SessionContentDto>();
        }
    }

    /// <summary>
    /// Extract core content from chat messages
    /// </summary>
    private string ExtractCoreContent(List<ChatMessage> messages)
    {
        // Extract user messages and assistant replies' key content
        var userMessages = messages
            .Where(m => m.ChatRole == ChatRole.User)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
            
        var summary = string.Join(" | ", userMessages.Take(3));
        if (summary.Length > 500)
        {
            summary = summary.Substring(0, 500) + "...";
        }
        
        return summary;
    }

    #endregion
}

