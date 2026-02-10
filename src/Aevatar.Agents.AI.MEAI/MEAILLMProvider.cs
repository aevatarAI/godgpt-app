using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.Telemetry;
using Aevatar.Agents.AI.WithTool;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.MEAI;

public sealed class MEAILLMProvider : AevatarLLMProviderBase
{
    private readonly IChatClient _chatClient;
    private readonly LLMProviderConfig _config;
    private readonly ILogger _logger;
    private readonly LLMCallPolicy _policy;

    public MEAILLMProvider(
        IChatClient chatClient,
        LLMProviderConfig config,
        ILogger logger,
        LLMCallPolicy? policy = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policy = policy ?? LLMCallPolicy.Default;
    }

    protected override LLMCallPolicy Policy => _policy;
    protected override ILogger? Logger => _logger;
    protected override string ProviderName => $"MEAI:{_config.Model}";

    /// <summary>
    /// Get model info - MEAI supports streaming for most models.
    /// </summary>
    public override Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AevatarModelInfo
        {
            Name = _config.Model,
            MaxTokens = _config.MaxTokens,
            SupportsStreaming = true,
            SupportsFunctions = true
        });
    }

    protected override async Task<AevatarLLMResponse> GenerateCoreAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = LLMTelemetry.StartGeneration(_config.Model);
        var sw = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("[MEAI] Calling model: {Model}, Endpoint: {Endpoint}",
                _config.Model, _config.Endpoint ?? "default");
            LLMTelemetry.RequestCount.Add(1, new KeyValuePair<string, object?>("model", _config.Model));

            var messageHistory = request.Messages?.Select(m => (
                Role: m.Role == AevatarChatRole.User ? "user" : "assistant",
                Content: m.Content ?? ""
            ));
            LLMTelemetry.RecordRequest(activity, request.SystemPrompt, request.UserPrompt, messageHistory);

            var messages = BuildChatMessages(request);
            var options = BuildChatOptions(request);

            // Diagnostic: estimate total request payload size
            long estimatedPayloadBytes = 0;
            foreach (var msg in messages)
            {
                if (msg.Contents != null)
                {
                    foreach (var content in msg.Contents)
                    {
                        if (content is Microsoft.Extensions.AI.TextContent tc)
                            estimatedPayloadBytes += System.Text.Encoding.UTF8.GetByteCount(tc.Text ?? "");
                        else if (content is Microsoft.Extensions.AI.DataContent dc)
                            estimatedPayloadBytes += (long)dc.Data.Length * 4 / 3;
                    }
                }
                else if (msg.Text != null)
                {
                    estimatedPayloadBytes += System.Text.Encoding.UTF8.GetByteCount(msg.Text);
                }
            }
            _logger.LogWarning(
                "[SIZE_DEBUG][MEAI_Sync] EstimatedPayload={PayloadMB:F2}MB ({PayloadBytes}bytes), Messages={MsgCount}, Model={Model}",
                estimatedPayloadBytes / (1024.0 * 1024.0), estimatedPayloadBytes, messages.Count, _config.Model);

            _logger.LogInformation("[MEAI] Sending request with {MsgCount} messages", messages.Count);
            var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
            _logger.LogInformation("[MEAI] Got response: Text='{Text}', MsgCount={MsgCount}",
                response.Text?.Substring(0, Math.Min(100, response.Text?.Length ?? 0)) ?? "(null)",
                response.Messages?.Count ?? 0);

            sw.Stop();
            activity?.SetTag("llm.duration_ms", sw.ElapsedMilliseconds);
            LLMTelemetry.ResponseTime.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("model", _config.Model));

            var result = CreateAevatarLLMResponse(response);
            LLMTelemetry.RecordResponse(activity, result.Content);

            if (result.Usage != null)
            {
                activity?.SetTag("llm.tokens.prompt", result.Usage.PromptTokens);
                activity?.SetTag("llm.tokens.completion", result.Usage.CompletionTokens);
                LLMTelemetry.InputTokens.Add(result.Usage.PromptTokens, new KeyValuePair<string, object?>("model", _config.Model));
                LLMTelemetry.OutputTokens.Add(result.Usage.CompletionTokens, new KeyValuePair<string, object?>("model", _config.Model));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MEAI] Error calling model {Model}: {Message}", _config.Model, ex.Message);
            LLMTelemetry.RecordError(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Maps Aevatar chat role to Microsoft.Extensions.AI chat role.
    /// </summary>
    private static ChatRole MapToMEAIChatRole(AevatarChatRole role) =>
        role == AevatarChatRole.User ? ChatRole.User : ChatRole.Assistant;

    /// <summary>
    /// Builds chat messages from request.
    /// When images are present, merges them into the last user message
    /// so text + images are in a single multimodal message (required by Gemini, etc.).
    /// </summary>
    private List<ChatMessage> BuildChatMessages(AevatarLLMRequest request)
    {
        var messages = new List<ChatMessage>();
        var hasImages = request.Images?.Count > 0;
        var imagesMerged = false;

        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));

        if (request.Messages?.Count > 0)
        {
            for (var i = 0; i < request.Messages.Count; i++)
            {
                var msg = request.Messages[i];
                var isLastMessage = i == request.Messages.Count - 1;

                // Merge images into the last user message for multimodal support
                if (isLastMessage && hasImages && msg.Role == AevatarChatRole.User)
                {
                    messages.Add(BuildUserMessageWithImages(msg.Content, request.Images));
                    imagesMerged = true;
                }
                else
                {
                    messages.Add(new ChatMessage(MapToMEAIChatRole(msg.Role), msg.Content));
                }
            }
        }

        // Fallback: no Messages provided, or images not merged (last msg was not user role)
        if (!imagesMerged && (!string.IsNullOrEmpty(request.UserPrompt) || hasImages))
        {
            messages.Add(BuildUserMessageWithImages(request.UserPrompt, request.Images));
        }

        return messages;
    }

    /// <summary>
    /// Builds a user message with optional image content for multimodal requests.
    /// </summary>
    private ChatMessage BuildUserMessageWithImages(string? userPrompt, IList<AevatarImageData>? images)
    {
        // If no images, return simple text message
        if (images == null || images.Count == 0)
        {
            return new ChatMessage(ChatRole.User, userPrompt ?? string.Empty);
        }

        // Build multimodal content with text and images
        var contents = new List<AIContent>();

        // Add text content first
        if (!string.IsNullOrEmpty(userPrompt))
        {
            contents.Add(new TextContent(userPrompt));
        }

        // Add image contents using MEAI's DataContent (ImageContent inherits from DataContent)
        foreach (var image in images)
        {
            var dataContent = new DataContent(image.Data, image.MediaType);
            contents.Add(dataContent);
            _logger.LogDebug("Added image to request: Key={Key}, MediaType={MediaType}, Size={Size} bytes",
                image.Key, image.MediaType, image.Data.Length);
        }

        _logger.LogInformation("[MEAI] Building multimodal message with {ImageCount} images", images.Count);
        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>
    /// Build chat options including temperature, max tokens, and tools.
    /// </summary>
    private ChatOptions BuildChatOptions(AevatarLLMRequest request)
    {
        var options = new ChatOptions
        {
            MaxOutputTokens = request.Settings?.MaxTokens ?? _config.MaxTokens,
            ModelId = _config.Model
        };

        // Only add tools if EnableTools is true (some proxy models don't support function calling)
        if (_config.EnableTools && request.Functions is { Count: > 0 })
        {
            var aiTools = ConvertFunctionsToAITools(request.Functions);
            if (aiTools.Count > 0)
            {
                options.Tools = aiTools;
                _logger.LogInformation("Added {Count} tools to ChatOptions", aiTools.Count);
            }
        }
        else if (!_config.EnableTools && request.Functions is { Count: > 0 })
        {
            _logger.LogDebug("Tools disabled for provider '{Provider}', skipping {Count} functions", 
                _config.Name, request.Functions.Count);
        }

        return options;
    }

    /// <summary>
    /// Convert Aevatar function definitions to Microsoft.Extensions.AI tools.
    /// </summary>
    private List<AITool> ConvertFunctionsToAITools(IList<AevatarFunctionDefinition> functions)
    {
        var aiTools = new List<AITool>();
        foreach (var func in functions)
        {
            var schema = ConvertParametersToJsonSchema(func.Parameters);
            var aiFunc = new DelegatingAIFunction(
                func.Name,
                func.Description,
                schema,
                (_, _) => Task.FromResult<object>(string.Format(ToolConstants.FunctionCalledMessageFormat, func.Name)));

            aiTools.Add(aiFunc);
        }

        return aiTools;
    }

    /// <summary>
    /// Custom AIFunction implementation that wraps our handler and exposes custom JsonSchema
    /// </summary>
    private sealed class DelegatingAIFunction : AIFunction
    {
        private readonly Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object>> _handler;
        private readonly string _name;
        private readonly string _description;
        private readonly JsonElement _jsonSchema;

        public DelegatingAIFunction(
            string name,
            string description,
            JsonElement jsonSchema,
            Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object>> handler)
        {
            _handler = handler;
            _name = name;
            _description = description;
            _jsonSchema = jsonSchema;
        }

        public override string Name => _name;
        public override string Description => _description;
        public override JsonElement JsonSchema => _jsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return await _handler(arguments, cancellationToken);
        }
    }

    private static JsonElement ConvertParametersToJsonSchema(Dictionary<string, AevatarParameterDefinition> parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in parameters)
        {
            var paramDef = param.Value;
            var property = new JsonObject
            {
                ["type"] = paramDef.Type,
                ["description"] = paramDef.Description
            };

            if (paramDef.Enum != null && paramDef.Enum.Count > 0)
            {
                var enumArray = new JsonArray();
                foreach (var val in paramDef.Enum)
                {
                    enumArray.Add(val);
                }

                property["enum"] = enumArray;
            }

            properties[param.Key] = property;

            if (paramDef.Required)
            {
                required.Add(param.Key);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };

        return JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString());
    }

    /// <summary>
    /// Unwraps function arguments that may be wrapped in a "_" key.
    /// </summary>
    private IDictionary<string, object?>? UnwrapFunctionArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count != 1 || !arguments.TryGetValue("_", out var wrappedValue))
            return arguments;

        if (wrappedValue is JsonElement wrappedElement &&
            wrappedElement.ValueKind == JsonValueKind.Object)
        {
            _logger.LogDebug("Unwrapping arguments from '_' key (JsonElement)");
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(wrappedElement.GetRawText());
        }

        if (wrappedValue is Dictionary<string, object?> wrappedDict)
        {
            _logger.LogDebug("Unwrapping arguments from '_' key (Dictionary)");
            return wrappedDict;
        }

        return arguments;
    }

    /// <summary>
    /// Processes function calls from the chat response.
    /// </summary>
    private AevatarFunctionCall? ProcessFunctionCalls(Microsoft.Extensions.AI.ChatResponse response)
    {
        if (response.Messages?.Count == 0)
            return null;

        foreach (var message in response.Messages!)
        {
            if (message.Contents?.Count == 0)
                continue;

            foreach (var content in message.Contents!)
            {
                if (content is not FunctionCallContent functionCall)
                    continue;

                _logger.LogDebug("Found function call: {FunctionName} with {ArgCount} arguments",
                    functionCall.Name, functionCall.Arguments?.Count ?? 0);

                var unwrappedArguments = UnwrapFunctionArguments(functionCall.Arguments);

                return new AevatarFunctionCall
                {
                    Name = functionCall.Name,
                    Arguments = unwrappedArguments != null
                        ? JsonSerializer.Serialize(unwrappedArguments)
                        : "{}"
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Creates AevatarLLMResponse from Microsoft.Extensions.AI ChatResponse.
    /// </summary>
    private AevatarLLMResponse CreateAevatarLLMResponse(Microsoft.Extensions.AI.ChatResponse response)
    {
        var content = response.Text;

        _logger.LogInformation(
            "[MEAI] ChatResponse structure: Text='{Text}', MessageCount={MsgCount}, ModelId={ModelId}",
            content ?? "(null)",
            response.Messages?.Count ?? 0,
            response.ModelId ?? "(null)");

        if (response.Messages?.Count > 0)
        {
            try
            {
                var messageList = response.Messages.ToList();
                _logger.LogInformation("[MEAI] Materialized {Count} messages", messageList.Count);

                for (var i = 0; i < messageList.Count; i++)
                {
                    var msg = messageList[i];
                    _logger.LogInformation(
                        "[MEAI] Message[{Index}]: Role={Role}, Text='{Text}', ContentsCount={ContentsCount}",
                        i,
                        msg.Role,
                        msg.Text ?? "(null)",
                        msg.Contents?.Count ?? 0);

                    if (msg.Contents != null)
                    {
                        var contentList = msg.Contents.ToList();
                        for (var j = 0; j < contentList.Count; j++)
                        {
                            var c = contentList[j];
                            var rawRepr = c.RawRepresentation?.ToString() ?? "(no raw)";
                            _logger.LogInformation("[MEAI] Content[{Index}]: Type={Type}, Text={Text}, Raw={Raw}",
                                j,
                                c.GetType().Name,
                                c is TextContent tc ? tc.Text ?? "(null)" : "(not text)",
                                rawRepr.Length > 200 ? rawRepr[..200] + "..." : rawRepr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MEAI] Error iterating messages: {Message}", ex.Message);
            }
        }

        if (string.IsNullOrEmpty(content) && response.Messages?.Count > 0)
        {
            content = ExtractTextFromMessages(response.Messages.ToList());
            _logger.LogInformation("[MEAI] Fallback extraction result: '{Content}'", content ?? "(null)");
        }

        var result = new AevatarLLMResponse
        {
            Content = content ?? string.Empty,
            ModelName = response.ModelId ?? _config.Model,
            AevatarStopReason = AevatarStopReason.Complete,
            Usage = CreateTokenUsage(
                (int?)response.Usage?.InputTokenCount,
                (int?)response.Usage?.OutputTokenCount,
                (int?)response.Usage?.TotalTokenCount)
        };

        var functionCall = ProcessFunctionCalls(response);
        if (functionCall != null)
        {
            result.AevatarFunctionCall = functionCall;
            result.Content = string.Format(ToolConstants.FunctionCalledMessageFormat, functionCall.Name);
        }

        return result;
    }

    /// <summary>
    /// Extract text content from ChatMessage collection.
    /// </summary>
    private static string? ExtractTextFromMessages(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            if (message.Role != ChatRole.Assistant)
                continue;

            if (!string.IsNullOrEmpty(message.Text))
            {
                sb.Append(message.Text);
                continue;
            }

            if (message.Contents == null)
                continue;

            foreach (var part in message.Contents)
            {
                if (part is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    sb.Append(textContent.Text);
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    protected override async IAsyncEnumerable<AevatarLLMToken> GenerateStreamCoreAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = LLMTelemetry.StartStreaming(_config.Model);
        var sw = Stopwatch.StartNew();
        var chunkIndex = 0;
        var totalTokens = 0;
        var firstTokenReceived = false;
        var responseBuilder = new StringBuilder();

        _logger.LogDebug("Generating streaming response using MEAI provider: {Model}", _config.Model);
        LLMTelemetry.RequestCount.Add(1,
            new KeyValuePair<string, object?>("model", _config.Model),
            new KeyValuePair<string, object?>("streaming", true));

        var messageHistory = request.Messages?.Select(m => (
            Role: m.Role == AevatarChatRole.User ? "user" : "assistant",
            Content: m.Content ?? ""
        ));
        LLMTelemetry.RecordRequest(activity, request.SystemPrompt, request.UserPrompt, messageHistory);

        var messages = BuildChatMessages(request);
        var options = BuildChatOptions(request);

        // Diagnostic: estimate total request payload size before sending to API
        long estimatedPayloadBytes = 0;
        foreach (var msg in messages)
        {
            if (msg.Contents != null)
            {
                foreach (var content in msg.Contents)
                {
                    if (content is Microsoft.Extensions.AI.TextContent textContent)
                    {
                        estimatedPayloadBytes += System.Text.Encoding.UTF8.GetByteCount(textContent.Text ?? "");
                    }
                    else if (content is Microsoft.Extensions.AI.DataContent dataContent)
                    {
                        // DataContent will be base64 encoded in the JSON request (~133% of raw bytes)
                        estimatedPayloadBytes += (long)dataContent.Data.Length * 4 / 3;
                    }
                }
            }
            else if (msg.Text != null)
            {
                estimatedPayloadBytes += System.Text.Encoding.UTF8.GetByteCount(msg.Text);
            }
        }
        _logger.LogWarning(
            "[SIZE_DEBUG][MEAI_Stream] EstimatedPayload={PayloadMB:F2}MB ({PayloadBytes}bytes), Messages={MsgCount}, Model={Model}",
            estimatedPayloadBytes / (1024.0 * 1024.0), estimatedPayloadBytes, messages.Count, _config.Model);

        await foreach (var chatUpdate in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            var chunk = ExtractStreamingText(chatUpdate);
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            if (!firstTokenReceived)
            {
                firstTokenReceived = true;
                var ttft = sw.ElapsedMilliseconds;
                LLMTelemetry.RecordFirstToken(activity, ttft);
                _logger.LogDebug("First token received in {TTFT}ms", ttft);
            }

            var estimatedTokens = Math.Max(1, chunk.Length / 4);
            totalTokens += estimatedTokens;
            chunkIndex++;

            responseBuilder.Append(chunk);
            LLMTelemetry.AddStreamingChunk(activity, chunkIndex, chunk.Length);

            yield return new AevatarLLMToken
            {
                Content = chunk,
                IsComplete = false
            };
        }

        sw.Stop();
        LLMTelemetry.CompleteStreaming(activity, chunkIndex, totalTokens, sw.ElapsedMilliseconds);
        LLMTelemetry.RecordStreamingResponse(activity, responseBuilder.ToString());
        _logger.LogDebug("Streaming complete: {Chunks} chunks, ~{Tokens} tokens in {Duration}ms",
            chunkIndex, totalTokens, sw.ElapsedMilliseconds);

        yield return new AevatarLLMToken { Content = string.Empty, IsComplete = true };
    }

    /// <summary>
    /// Extracts streaming text from chat update using reflection.
    /// </summary>
    private string ExtractStreamingText(object chatUpdate)
    {
        if (chatUpdate == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        var reasoningContent = ExtractReasoningContent(chatUpdate);
        if (!string.IsNullOrEmpty(reasoningContent))
        {
            _logger.LogDebug("[REASONING] {Content}", reasoningContent);
        }

        var text = StreamingPropertyCache.GetValue(chatUpdate, "TextDelta") as string;
        if (string.IsNullOrEmpty(text))
        {
            text = StreamingPropertyCache.GetValue(chatUpdate, "Text") as string;
        }

        if (!string.IsNullOrEmpty(text))
        {
            sb.Append(text);
            return sb.ToString();
        }

        var message = StreamingPropertyCache.GetValue(chatUpdate, "Message");
        if (message == null)
        {
            return sb.ToString();
        }

        text = StreamingPropertyCache.GetValue(message, "Text") as string;
        if (!string.IsNullOrEmpty(text))
        {
            sb.Append(text);
            return sb.ToString();
        }

        var content = StreamingPropertyCache.GetValue(message, "Content") as System.Collections.IEnumerable;
        if (content == null)
        {
            return sb.ToString();
        }

        foreach (var part in content)
        {
            if (part == null) continue;
            var partText = StreamingPropertyCache.GetValue(part, "Text") as string;
            if (!string.IsNullOrEmpty(partText))
            {
                sb.Append(partText);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract reasoning_content from DeepSeek-reasoner or similar reasoning models.
    /// </summary>
    private static string? ExtractReasoningContent(object chatUpdate)
    {
        var reasoning = StreamingPropertyCache.GetValue(chatUpdate, "ReasoningContent") as string;
        if (!string.IsNullOrEmpty(reasoning))
        {
            return reasoning;
        }

        var delta = StreamingPropertyCache.GetValue(chatUpdate, "Delta");
        if (delta != null)
        {
            reasoning = StreamingPropertyCache.GetValue(delta, "ReasoningContent") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }

            reasoning = StreamingPropertyCache.GetValue(delta, "reasoning_content") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }
        }

        var message = StreamingPropertyCache.GetValue(chatUpdate, "Message");
        if (message != null)
        {
            reasoning = StreamingPropertyCache.GetValue(message, "ReasoningContent") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }
        }

        return null;
    }
}
