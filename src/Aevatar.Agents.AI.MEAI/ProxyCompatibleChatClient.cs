using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.MEAI;

/// <summary>
/// A delegating chat client that provides compatibility with proxies like hyperecho-proxy.
/// Uses raw HttpClient for streaming to bypass OpenAI SDK's SSE parsing issues.
/// Non-streaming calls delegate to the underlying IChatClient.
/// </summary>
public sealed class ProxyCompatibleChatClient : DelegatingChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger? _logger;

    public ProxyCompatibleChatClient(
        IChatClient innerClient,
        string endpoint,
        string apiKey,
        string model,
        ILogger? logger = null) : base(innerClient)
    {
        _endpoint = endpoint?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(endpoint));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _logger = logger;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Streaming: use HttpClient directly to bypass OpenAI SDK's SSE parsing issues
    /// </summary>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.ToList();
        var requestBody = BuildRequestBody(messages, options);
        var requestJson = JsonSerializer.Serialize(requestBody);

        _logger?.LogDebug("[ProxyCompatibleChatClient] Streaming request to {Endpoint}", _endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: "))
                continue;

            var data = line[6..]; // Remove "data: " prefix

            if (data == "[DONE]")
            {
                _logger?.LogDebug("[ProxyCompatibleChatClient] Stream completed");
                yield break;
            }

            ChatResponseUpdate? update = null;
            try
            {
                update = ParseStreamingUpdate(data);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "[ProxyCompatibleChatClient] Failed to parse SSE data: {Data}", 
                    data.Length > 100 ? data[..100] + "..." : data);
                continue;
            }

            if (update != null)
            {
                yield return update;
            }
        }
    }

    private object BuildRequestBody(IList<ChatMessage> chatMessages, ChatOptions? options)
    {
        var messages = chatMessages.Select(m => new
        {
            role = m.Role.Value.ToLowerInvariant(),
            content = GetMessageContent(m)
        }).ToList();

        var body = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["stream"] = true
        };

        if (options?.Temperature.HasValue == true)
            body["temperature"] = options.Temperature.Value;

        if (options?.MaxOutputTokens.HasValue == true)
            body["max_tokens"] = options.MaxOutputTokens.Value;

        if (options?.TopP.HasValue == true)
            body["top_p"] = options.TopP.Value;

        return body;
    }

    private static string GetMessageContent(ChatMessage message)
    {
        if (message.Contents.Count == 0)
            return string.Empty;

        var textContent = message.Contents
            .OfType<TextContent>()
            .FirstOrDefault();

        return textContent?.Text ?? string.Empty;
    }

    private ChatResponseUpdate? ParseStreamingUpdate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var choice = choices[0];
        
        if (!choice.TryGetProperty("delta", out var delta))
            return null;

        string? content = null;
        string? role = null;
        string? finishReason = null;

        if (delta.TryGetProperty("content", out var contentElement))
            content = contentElement.GetString();

        if (delta.TryGetProperty("role", out var roleElement))
            role = roleElement.GetString();

        if (choice.TryGetProperty("finish_reason", out var finishElement) && 
            finishElement.ValueKind != JsonValueKind.Null)
            finishReason = finishElement.GetString();

        // Skip empty updates
        if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(role) && finishReason == null)
            return null;

        var update = new ChatResponseUpdate
        {
            Role = !string.IsNullOrEmpty(role) ? new ChatRole(role) : null,
            FinishReason = ParseFinishReason(finishReason)
        };
        
        // Add text content if present
        if (!string.IsNullOrEmpty(content))
        {
            update.Contents.Add(new TextContent(content));
        }

        return update;
    }

    private static ChatFinishReason? ParseFinishReason(string? reason)
    {
        return reason?.ToLowerInvariant() switch
        {
            "stop" => ChatFinishReason.Stop,
            "length" => ChatFinishReason.Length,
            "content_filter" => ChatFinishReason.ContentFilter,
            "tool_calls" or "function_call" => ChatFinishReason.ToolCalls,
            _ => null
        };
    }
}
