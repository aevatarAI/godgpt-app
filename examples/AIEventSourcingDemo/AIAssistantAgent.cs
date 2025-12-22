using System.Text;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace AIEventSourcingDemo;

/// <summary>
/// AI Assistant Agent with Event Sourcing
/// Demonstrates how AI decisions and state changes are captured as events
/// </summary>
public class AIAssistantAgent : AIGAgentBase<AIAssistantState, AIAssistantConfig>
{
    // Current conversation context
    private string? _currentConversationId;
    private int _currentMessageCount;
    private int _currentTokenCount;
    private bool _localInitialized = false; // Local tracking, base class has its own _isInitialized
    private readonly Dictionary<string, DateTime> _conversationStartTimes = new();
    private readonly Dictionary<string, List<double>> _responseMetrics = new();

    // 必须有无参构造函数供框架创建实例
    public AIAssistantAgent() : base()
    {
        // 构造函数中记录日志，确认实例创建
        Console.WriteLine($"[DEBUG] AIAssistantAgent constructor called (no params)");
    }

    public AIAssistantAgent(string id) : base(id)
    {
        Console.WriteLine($"[DEBUG] AIAssistantAgent constructor called with ID: {id}");
    }

    /// <summary>
    /// System prompt for the AI assistant
    /// </summary>
    public override string SystemPrompt => $@"
You are {CustomConfig.Personality}.

Your capabilities include:
{string.Join("\n", CustomConfig.Capabilities.Select(c => $"- {c}"))}

Creativity level: {CustomConfig.CreativityLevel}
Learning enabled: {CustomConfig.EnableLearning}

Provide helpful, accurate, and engaging responses.
Be concise but thorough.
";

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"[DEBUG] OnActivateAsync called for AIAssistantAgent");
        await base.OnActivateAsync(ct);

        // Log discovered event handlers
        var handlers = GetEventHandlers();
        Console.WriteLine($"[DEBUG] Discovered {handlers.Length} event handlers:");
        foreach (var handler in handlers)
        {
            Console.WriteLine($"[DEBUG]   - {handler.Method.Name}");
        }

        CustomConfig.Personality = "Friendly and helpful AI assistant";
        CustomConfig.Capabilities.Add("General knowledge");
        CustomConfig.Capabilities.Add("Code assistance");
        CustomConfig.Capabilities.Add("Creative writing");
        CustomConfig.CreativityLevel = 0.7;
        CustomConfig.EnableLearning = true;
    }

    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        Console.WriteLine(
            $"[DEBUG] AIAssistantAgent.HandleEventAsync called with event type: {envelope.Payload?.TypeUrl}");
        await base.HandleEventAsync(envelope, ct);
    }

    /// <summary>
    /// Event handler for initialization event
    /// </summary>
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleInitialization(AssistantInitializedEvent evt)
    {
        Console.WriteLine($"[DEBUG] HandleInitialization called with event: {evt.Name}");
        Logger?.LogInformation("Assistant {Name} initializing with ID {Id}", evt.Name, evt.AssistantId);

        // IMPORTANT: Raise the event internally so it triggers TransitionState
        RaiseEvent(evt);

        // Initialize AI capabilities if not already done
        if (!_localInitialized)
        {
            try
            {
                // Initialize with configured LLM provider from appsettings
                Console.WriteLine("[DEBUG] Attempting to initialize AI with provider: deepseek");
                await InitializeAsync(
                    "deepseek", // Use the provider configured in appsettings.json
                    config =>
                    {
                        config.Model = "deepseek-chat";
                        config.Temperature = 0.8f;
                        config.MaxOutputTokens = 1500;
                    });

                _localInitialized = true;
                Console.WriteLine($"[DEBUG] AI INITIALIZED SUCCESSFULLY! _localInitialized = {_localInitialized}");
                Logger?.LogInformation("AI capabilities initialized for assistant {Name}", evt.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] FAILED TO INITIALIZE AI! Error: {ex.Message}");
                Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
                Logger?.LogError(ex, "Failed to initialize AI capabilities for assistant {Name}", evt.Name);
                _localInitialized = false;
            }
        }

        // Commit the events to trigger state transitions
        await ConfirmEventsAsync();

        // State transition is handled by TransitionState method
    }

    /// <summary>
    /// Event handler for user messages - generates AI response
    /// </summary>
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleUserMessage(UserMessageReceived evt)
    {
        Console.WriteLine($"[DEBUG] HandleUserMessage called for conversation: {evt.ConversationId}");
        Console.WriteLine($"[DEBUG] Message: {evt.Message}");
        Console.WriteLine($"[DEBUG] Is initialized: {_localInitialized}");
        Logger?.LogInformation("Processing user message in conversation {ConversationId}", evt.ConversationId);

        // IMPORTANT: Raise the event internally so it triggers TransitionState
        // This updates State.TotalInteractions
        RaiseEvent(evt);

        // Update conversation context
        if (_currentConversationId != evt.ConversationId)
        {
            _currentConversationId = evt.ConversationId;
            _currentMessageCount = 0;
            _currentTokenCount = 0;
        }

        _currentMessageCount++;

        // Generate AI response (if initialized)
        if (_localInitialized)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Creating chat request for message: {evt.Message}");
                var chatRequest = CreateChatRequest(evt.Message);
                Console.WriteLine($"[DEBUG] Calling ChatStreamAsync...");

                var sb = new StringBuilder();
                Console.Write("🤖 [AI STREAM]: "); // Visual indicator for stream start

                // Add timeout to prevent hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                _conversationStartTimes[evt.ConversationId] = DateTime.UtcNow;

                // Stream the response
                await foreach (var chunk in ChatStreamAsync(chatRequest, cts.Token))
                {
                    Console.Write(chunk);
                    sb.Append(chunk);
                }

                Console.WriteLine(); // End of stream newline

                var responseContent = sb.ToString();

                // Raise response generated event
                // Simple token estimation for demo purposes
                var tokensUsed = responseContent.Length / 4;
                _currentTokenCount += tokensUsed;

                RaiseEvent(new AssistantResponseGenerated
                {
                    Response = responseContent,
                    TokensUsed = tokensUsed,
                    ConfidenceScore = 0.95,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                // Commit events
                await ConfirmEventsAsync();

                Logger?.LogInformation("Generated response for conversation {ConversationId}, Tokens: {Tokens}",
                    evt.ConversationId, tokensUsed);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"\n[DEBUG] ChatStreamAsync TIMEOUT after 60 seconds!");
                Logger?.LogError("AI response timed out for conversation {ConversationId}", evt.ConversationId);

                // Generate a timeout response
                RaiseEvent(new AssistantResponseGenerated
                {
                    Response = "I apologize, but I'm taking too long to think. Please try again.",
                    TokensUsed = 0,
                    ConfidenceScore = 0.0,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });
                await ConfirmEventsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[DEBUG] Error generating response: {ex.Message}");
                Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
                Logger?.LogError(ex, "Error generating response for conversation {ConversationId}", evt.ConversationId);

                // Generate an error response
                RaiseEvent(new AssistantResponseGenerated
                {
                    Response = "I encountered an error while processing your request. Please try again.",
                    TokensUsed = 0,
                    ConfidenceScore = 0.0,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });
                await ConfirmEventsAsync();
            }
        }
        else
        {
            Console.WriteLine($"[DEBUG] AI NOT INITIALIZED! Cannot generate response");
            Logger?.LogWarning("AI not initialized, cannot generate response for conversation {ConversationId}",
                evt.ConversationId);
        }
    }

    /// <summary>
    /// Event handler for assistant responses - performs post-processing
    /// </summary>
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleAssistantResponse(AssistantResponseGenerated evt)
    {
        Logger?.LogInformation("Processing assistant response with {Tokens} tokens", evt.TokensUsed);

        // Analyze response quality
        var responseQuality = AnalyzeResponseQuality(evt.Response, evt.ConfidenceScore);

        // If low confidence, could trigger a refinement process
        if (evt.ConfidenceScore < 0.7)
        {
            Logger?.LogWarning("Low confidence response ({Score:F2}), consider refining", evt.ConfidenceScore);

            // Raise an event for potential refinement
            RaiseEvent(new ResponseQualityEvent
            {
                ResponseId = Guid.NewGuid().ToString(),
                Quality = responseQuality,
                NeedsRefinement = true,
                OriginalResponse = evt.Response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        // Track token usage for cost management
        if (_currentTokenCount > 10000) // Token threshold
        {
            Logger?.LogWarning("High token usage in conversation: {Tokens}", _currentTokenCount);

            // Could trigger conversation summarization to reduce context
            await TriggerConversationSummarization();
        }

        // Update metrics
        UpdateResponseMetrics(evt.TokensUsed, evt.ConfidenceScore);

        // Note: This event is already raised by HandleUserMessage, so we don't raise it again
        // But we may need to commit any additional events raised in this handler
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Event handler for feedback - triggers learning and improvement
    /// </summary>
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleFeedback(FeedbackReceived evt)
    {
        Logger?.LogInformation("Processing feedback for conversation {ConversationId}: {Score}/5.0",
            evt.ConversationId, evt.SatisfactionScore);

        // IMPORTANT: Raise the event internally so it triggers TransitionState
        RaiseEvent(evt);

        // Analyze feedback sentiment
        var sentiment = AnalyzeFeedbackSentiment(evt.FeedbackText);

        // Low satisfaction triggers improvement process
        if (evt.SatisfactionScore < 3.0)
        {
            Logger?.LogWarning("Low satisfaction score {Score} - initiating improvement process",
                evt.SatisfactionScore);

            // Extract conversation context for learning
            var conversationContext = GetConversationContext(evt.ConversationId);

            // Raise learning event
            RaiseEvent(new LearningTriggeredEvent
            {
                ConversationId = evt.ConversationId,
                SatisfactionScore = evt.SatisfactionScore,
                FeedbackText = evt.FeedbackText,
                Sentiment = sentiment,
                Context = conversationContext,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            // Adjust AI parameters for better performance
            await AdjustAIParameters(evt.SatisfactionScore, sentiment);
        }
        else if (evt.SatisfactionScore >= 4.5)
        {
            Logger?.LogInformation("High satisfaction - marking successful pattern");

            // Record successful interaction pattern
            RaiseEvent(new SuccessfulPatternEvent
            {
                ConversationId = evt.ConversationId,
                Pattern = ExtractConversationPattern(evt.ConversationId),
                Score = evt.SatisfactionScore,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        // Update quality metrics
        UpdateQualityMetrics(evt.SatisfactionScore, sentiment);

        // Commit the events to trigger state transitions
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Event handler for conversation completion - performs analytics and cleanup
    /// </summary>
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleConversationComplete(ConversationCompleted evt)
    {
        Logger?.LogInformation("Completing conversation {ConversationId}: {Messages} messages, {Tokens} tokens",
            evt.ConversationId, evt.TotalMessages, evt.TotalTokens);

        // IMPORTANT: Raise the event internally so it triggers TransitionState
        RaiseEvent(evt);

        // Generate conversation analytics
        var duration = CalculateConversationDuration(evt.ConversationId);
        var analytics = new ConversationAnalyticsData
        {
            ConversationId = evt.ConversationId,
            DurationSeconds = (long)duration.TotalSeconds,
            AverageResponseTime = CalculateAverageResponseTime(evt.ConversationId),
            TokenEfficiency = (double)evt.TotalMessages / evt.TotalTokens,
            SatisfactionScore = evt.FinalSatisfaction,
            CostEstimate = CalculateConversationCost(evt.TotalTokens)
        };

        // Raise analytics event
        RaiseEvent(new ConversationAnalyticsEvent
        {
            ConversationId = evt.ConversationId,
            Analytics = analytics,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        // Archive conversation if needed
        if (evt.TotalMessages > 20 || evt.TotalTokens > 5000)
        {
            Logger?.LogInformation("Archiving large conversation {ConversationId}", evt.ConversationId);
            await ArchiveConversation(evt.ConversationId);
        }

        // Clean up conversation context
        if (_currentConversationId == evt.ConversationId)
        {
            _currentConversationId = null;
            _currentMessageCount = 0;
            _currentTokenCount = 0;
        }

        // Trigger periodic maintenance if needed
        if (CustomState.TotalInteractions % 100 == 0)
        {
            Logger?.LogInformation("Triggering periodic maintenance after {Count} interactions",
                CustomState.TotalInteractions);
            await PerformPeriodicMaintenance();
        }

        // Commit the events to trigger state transitions
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Pure functional state transitions for event sourcing
    /// </summary>
    protected override void TransitionState(AIAssistantState state, IMessage evt)
    {
        switch (evt)
        {
            case AssistantInitializedEvent initEvent:
                state.AssistantId = initEvent.AssistantId;
                state.Name = initEvent.Name;
                state.CreatedAt = initEvent.CreatedAt;
                state.TotalInteractions = 0;
                state.AverageSatisfaction = 0;
                break;

            case UserMessageReceived userMessage:
                state.TotalInteractions++;
                state.LastInteraction = Timestamp.FromDateTime(DateTime.UtcNow);
                break;

            case AssistantResponseGenerated response:
                // Could track response metrics here
                break;

            case FeedbackReceived feedback:
                // Update average satisfaction
                var currentTotal = state.AverageSatisfaction * (state.TotalInteractions - 1);
                state.AverageSatisfaction = (currentTotal + feedback.SatisfactionScore) / state.TotalInteractions;
                break;

            case ConversationCompleted completed:
                // Add to conversation history
                state.ConversationHistory.Add(new ConversationSummary
                {
                    ConversationId = completed.ConversationId,
                    UserId = "user", // Would be from actual user context
                    Topic = "General", // Would be determined by AI
                    MessageCount = completed.TotalMessages,
                    TokensUsed = completed.TotalTokens,
                    SatisfactionScore = completed.FinalSatisfaction,
                    Timestamp = completed.Timestamp
                });

                // Keep only last 100 conversations
                while (state.ConversationHistory.Count > 100)
                {
                    state.ConversationHistory.RemoveAt(0);
                }

                break;
        }
    }

    /// <summary>
    /// Auto-confirm events after AI operations
    /// </summary>
    protected override bool AutoConfirmEvents => true;

    /// <summary>
    /// Snapshot every 50 events
    /// </summary>
    protected override Aevatar.Agents.Core.EventSourcing.ISnapshotStrategy SnapshotStrategy =>
        new Aevatar.Agents.Core.EventSourcing.IntervalSnapshotStrategy(50);

    /// <summary>
    /// Get agent description
    /// </summary>
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"AI Assistant '{CustomState.Name}' - {CustomState.TotalInteractions} interactions, " +
                               $"Satisfaction: {CustomState.AverageSatisfaction:F2}/5.0");
    }

    #region Helper Methods

    private string AnalyzeResponseQuality(string response, double confidenceScore)
    {
        if (confidenceScore > 0.9) return "Excellent";
        if (confidenceScore > 0.7) return "Good";
        if (confidenceScore > 0.5) return "Acceptable";
        return "Poor";
    }

    private async Task TriggerConversationSummarization()
    {
        Logger?.LogInformation("Triggering conversation summarization to reduce context");
        // In production, would call AI to summarize
        await Task.CompletedTask;
    }

    private void UpdateResponseMetrics(int tokensUsed, double confidence)
    {
        if (!_responseMetrics.ContainsKey(_currentConversationId ?? ""))
            _responseMetrics[_currentConversationId ?? ""] = new List<double>();

        _responseMetrics[_currentConversationId ?? ""].Add(confidence);
    }

    private string AnalyzeFeedbackSentiment(string feedbackText)
    {
        if (string.IsNullOrEmpty(feedbackText)) return "Neutral";

        var lowerText = feedbackText.ToLower();
        if (lowerText.Contains("great") || lowerText.Contains("excellent") || lowerText.Contains("perfect"))
            return "Positive";
        if (lowerText.Contains("bad") || lowerText.Contains("poor") || lowerText.Contains("terrible"))
            return "Negative";
        return "Neutral";
    }

    private string GetConversationContext(string conversationId)
    {
        // Extract conversation context from state
        var conversation = CustomState.ConversationHistory.FirstOrDefault(c => c.ConversationId == conversationId);
        return conversation?.Topic ?? "No context available";
    }

    private async Task AdjustAIParameters(double satisfactionScore, string sentiment)
    {
        if (satisfactionScore < 3.0 && Config != null)
        {
            // Adjust temperature for better responses
            var currentTemp = Config.Temperature;
            Config.Temperature = (float)
                Math.Max(0.3, currentTemp - 0.1); // Lower temperature for more focused responses
            Logger?.LogInformation("Adjusted AI temperature from {Old} to {New}", currentTemp,
                Config.Temperature);
        }

        await Task.CompletedTask;
    }

    private string ExtractConversationPattern(string conversationId)
    {
        // Extract successful pattern from conversation
        return $"Pattern_{conversationId}_Success";
    }

    private void UpdateQualityMetrics(double satisfactionScore, string sentiment)
    {
        Logger?.LogDebug("Updating quality metrics - Score: {Score}, Sentiment: {Sentiment}",
            satisfactionScore, sentiment);
    }

    private TimeSpan CalculateConversationDuration(string conversationId)
    {
        if (_conversationStartTimes.TryGetValue(conversationId, out var startTime))
        {
            return DateTime.UtcNow - startTime;
        }

        return TimeSpan.FromMinutes(5); // Default estimate
    }

    private double CalculateAverageResponseTime(string conversationId)
    {
        // In production, would track individual response times
        return 1.5; // seconds
    }

    private double CalculateConversationCost(int totalTokens)
    {
        // Estimate based on typical pricing
        var costPer1000Tokens = 0.002; // $0.002 per 1K tokens
        return (totalTokens / 1000.0) * costPer1000Tokens;
    }

    private async Task ArchiveConversation(string conversationId)
    {
        Logger?.LogInformation("Archiving conversation {ConversationId}", conversationId);
        // In production, would move to cold storage
        await Task.CompletedTask;
    }

    private async Task PerformPeriodicMaintenance()
    {
        Logger?.LogInformation("Performing periodic maintenance");

        // Clean up old conversation contexts
        _conversationStartTimes.Clear();
        _responseMetrics.Clear();

        // Could trigger snapshot creation
        // Note: Snapshot creation is handled by the base class periodically

        Logger?.LogInformation("Periodic maintenance completed");
    }

    #endregion
}