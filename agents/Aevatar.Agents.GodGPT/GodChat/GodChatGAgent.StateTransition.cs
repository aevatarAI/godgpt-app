using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Application.Grains.GodChat;
using Google.Protobuf;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// State transition handling for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    /// <summary>
    /// New framework TransitionState - handles Protobuf events
    /// Keep original logic structure, just change type names
    /// </summary>
    protected override void TransitionState(GodChatStateProto state, IMessage @event)
    {
        switch (@event)
        {
            case UpdateUserProfileEvent evt:
                if (state.UserProfile == null)
                {
                    state.UserProfile = new UserProfileProto();
                }
                state.UserProfile.Gender = evt.Gender;
                state.UserProfile.BirthDate = evt.BirthDate;
                state.UserProfile.BirthPlace = evt.BirthPlace;
                state.UserProfile.FullName = evt.FullName;
                break;
                
            case RenameChatTitleEvent evt:
                state.Title = evt.Title;
                break;
                
            case SetChatManagerGuidEvent evt:
                state.ChatManagerGuid = evt.ChatManagerGuid;
                break;
                
            case UpdateRegionProxiesEvent evt:
                state.RegionProxies.Clear();
                state.RegionProxies.AddRange(evt.RegionProxies);
                break;
                
            case UpdateSingleRegionProxyEvent evt:
                var existingRegion = state.RegionProxies.FirstOrDefault(r => r.Region == evt.Region);
                if (existingRegion != null)
                {
                    existingRegion.ProxyIds.Clear();
                    existingRegion.ProxyIds.AddRange(evt.ProxyIds);
                }
                else
                {
                    state.RegionProxies.Add(new RegionProxiesEntryProto
                    {
                        Region = evt.Region,
                        ProxyIds = { evt.ProxyIds }
                    });
                }
                break;
                
            case UpdateChatTimeEvent evt:
                if (state.FirstChatTime == null)
                {
                    state.FirstChatTime = evt.ChatTime;
                }
                state.LastChatTime = evt.ChatTime;
                break;
                
            case AddChatMessageMetasEvent evt:
                if (evt.ChatMessageMetas != null && evt.ChatMessageMetas.Any())
                {
                    // Calculate the starting index for new metadata based on current ChatHistory count
                    // minus the number of new metadata items we're adding
                    int newMetadataCount = evt.ChatMessageMetas.Count;
                    int targetStartIndex = Math.Max(0, state.ChatHistory.Count - newMetadataCount);

                    // Ensure we have enough default metadata up to the target start index
                    while (state.ChatMessageMetas.Count < targetStartIndex)
                    {
                        state.ChatMessageMetas.Add(GodChatConversions.CreateDefaultMetaProto());
                    }

                    // Add the new metadata
                    foreach (var meta in evt.ChatMessageMetas)
                    {
                        state.ChatMessageMetas.Add(meta);
                    }
                }

                // Final sync: ensure ChatMessageMetas matches ChatHistory count
                while (state.ChatMessageMetas.Count < state.ChatHistory.Count)
                {
                    state.ChatMessageMetas.Add(GodChatConversions.CreateDefaultMetaProto());
                }
                break;
                
            case AddPromptTemplateEvent evt:
                if (string.IsNullOrEmpty(evt.PromptTemplate))
                {
                    break;
                }
                state.PromptTemplate = evt.PromptTemplate;
                break;
                
            case AddChatMessagesEvent evt:
                if (evt.Messages.Count > 0)
                {
                    state.ChatHistory.AddRange(evt.Messages);
                }

                var maxChatHistoryCount = 32;
                if (state.MaxHistoryCount > 0)
                {
                    maxChatHistoryCount = state.MaxHistoryCount;
                }

                // Trim chat history if needed (pure state transition, no side effects)
                while (state.ChatHistory.Count > maxChatHistoryCount)
                {
                    state.ChatHistory.RemoveAt(0);
                }
                break;
                
            case SetMaxHistoryCountEvent evt:
                state.MaxHistoryCount = evt.MaxHistoryCount;
                break;
                
            case UpdateProxyInitStatusEvent evt:
                var existingStatus = state.ProxyInitStatuses.FirstOrDefault(p => p.ProxyId == evt.ProxyId);
                if (existingStatus != null)
                {
                    existingStatus.Status = evt.Status;
                }
                else
                {
                    state.ProxyInitStatuses.Add(new ProxyInitStatusEntryProto
                    {
                        ProxyId = evt.ProxyId,
                        Status = evt.Status
                    });
                }
                break;
                
            case PerformConfigCombinedEvent evt:
                // Handle combined config event - equivalent to the three separate events
                // 1. Update region proxy
                var configRegion = state.RegionProxies.FirstOrDefault(r => r.Region == evt.Region);
                if (configRegion != null)
                {
                    configRegion.ProxyIds.Clear();
                    configRegion.ProxyIds.AddRange(evt.ProxyIds);
                }
                else
                {
                    state.RegionProxies.Add(new RegionProxiesEntryProto
                    {
                        Region = evt.Region,
                        ProxyIds = { evt.ProxyIds }
                    });
                }
                
                // 2. AddPromptTemplateEvent equivalent
                if (!string.IsNullOrEmpty(evt.PromptTemplate))
                {
                    state.PromptTemplate = evt.PromptTemplate;
                }
                
                // 3. SetMaxHistoryCountEvent equivalent
                state.MaxHistoryCount = evt.MaxHistoryCount;
                break;
                
            case StreamChatCombinedEvent evt:
                // Handle combined stream chat event - equivalent to the three separate events
                // 1. AddChatMessagesEvent equivalent
                if (evt.ChatList.Count > 0)
                {
                    state.ChatHistory.AddRange(evt.ChatList);
                }

                var maxHistoryCount = 32;
                if (state.MaxHistoryCount > 0)
                {
                    maxHistoryCount = state.MaxHistoryCount;
                }

                // Trim chat history (pure state transition)
                while (state.ChatHistory.Count > maxHistoryCount)
                {
                    state.ChatHistory.RemoveAt(0);
                }
                
                // 2. UpdateChatTimeEvent equivalent
                if (state.FirstChatTime == null)
                {
                    state.FirstChatTime = evt.ChatTime;
                }
                state.LastChatTime = evt.ChatTime;
                
                // 3. AddChatMessageMetasEvent equivalent
                if (evt.ChatMessageMetas != null && evt.ChatMessageMetas.Any())
                {
                    // Calculate the starting index for new metadata based on current ChatHistory count
                    // minus the number of new metadata items we're adding
                    int newMetadataCount = evt.ChatMessageMetas.Count;
                    int targetStartIndex = Math.Max(0, state.ChatHistory.Count - newMetadataCount);
                    
                    // Ensure we have enough default metadata up to the target start index
                    while (state.ChatMessageMetas.Count < targetStartIndex)
                    {
                        state.ChatMessageMetas.Add(GodChatConversions.CreateDefaultMetaProto());
                    }
                    
                    // Add the new metadata
                    foreach (var meta in evt.ChatMessageMetas)
                    {
                        state.ChatMessageMetas.Add(meta);
                    }
                }
                
                // Final sync: ensure ChatMessageMetas matches ChatHistory count
                while (state.ChatMessageMetas.Count < state.ChatHistory.Count)
                {
                    state.ChatMessageMetas.Add(GodChatConversions.CreateDefaultMetaProto());
                }
                break;
        }
    }
}

