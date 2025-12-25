using System.Net.Http;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools.CustomTools;

/// <summary>
/// HTTP请求工具实现示例
/// 展示如何使用 IAevatarTool 接口创建自定义工具
/// </summary>
public class HttpRequestTool : AevatarToolBase
{
    private readonly HttpClient _httpClient;
    
    /// <inheritdoc />
    public override string Name => "http_request";
    
    /// <inheritdoc />
    public override string Description => "Make HTTP requests to external APIs with full control over method, headers, and body";
    
    /// <inheritdoc />
    public override ToolCategory Category => ToolCategory.Integration;
    
    /// <inheritdoc />
    public override string Version => "1.0.0";
    
    /// <inheritdoc />
    public override IList<string> Tags => new List<string> { "http", "api", "rest", "external", "integration" };
    
    /// <inheritdoc />
    protected override bool RequiresInternalAccess() => false;
    
    /// <inheritdoc />
    protected override bool RequiresConfirmation() => true; // 外部请求需要确认
    
    /// <inheritdoc />
    protected override TimeSpan? GetTimeout() => TimeSpan.FromSeconds(30);
    
    /// <inheritdoc />
    protected override int? GetRateLimit() => 100; // 每分钟最多100次请求
    
    /// <summary>
    /// 构造函数
    /// </summary>
    public HttpRequestTool(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }
    
    /// <inheritdoc />
    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["url"] = new()
                {
                    Type = "string",
                    Required = true,
                    Description = "The URL to send the request to"
                },
                ["method"] = new()
                {
                    Type = "string",
                    Enum = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" },
                    DefaultValue = "GET",
                    Description = "HTTP method to use"
                },
                ["headers"] = new()
                {
                    Type = "object",
                    Description = "HTTP headers as key-value pairs"
                },
                ["body"] = new()
                {
                    Type = "string",
                    Description = "Request body for POST, PUT, PATCH methods"
                },
                ["content_type"] = new()
                {
                    Type = "string",
                    DefaultValue = "application/json",
                    Description = "Content-Type header value"
                },
                ["timeout"] = new()
                {
                    Type = "integer",
                    DefaultValue = 30,
                    Description = "Request timeout in seconds (5-120)"
                },
                ["follow_redirects"] = new()
                {
                    Type = "boolean",
                    DefaultValue = true,
                    Description = "Whether to follow HTTP redirects"
                }
            },
            Required = new[] { "url" }
        };
    }
    
    /// <inheritdoc />
    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // 验证参数
        var validation = ValidateParameters(ToNullableParameters(parameters));
        if (!validation.IsValid)
        {
            logger?.LogWarning("Invalid parameters: {Errors}", string.Join(", ", validation.Errors));
            var errorResult = new { success = false, errors = validation.Errors };
            var errorJson = JsonSerializer.Serialize(errorResult);
            return JsonParser.Default.Parse<Struct>(errorJson);
        }
        
        try
        {
            var url = parameters["url"]?.ToString();
            var method = parameters.GetValueOrDefault("method", "GET")?.ToString() ?? "GET";
            var headers = ParseHeaders(parameters.GetValueOrDefault("headers"));
            var body = parameters.GetValueOrDefault("body")?.ToString();
            var contentType = parameters.GetValueOrDefault("content_type", "application/json")?.ToString();
            var timeout = ParseTimeout(parameters.GetValueOrDefault("timeout", 30));
            var followRedirects = ParseBool(parameters.GetValueOrDefault("follow_redirects", true));
            
            logger?.LogInformation("Making HTTP {Method} request to {Url}", method, url);
            
            // 创建请求
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            
            // 添加headers
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            
            // 添加body
            if (!string.IsNullOrEmpty(body) && method != "GET" && method != "HEAD")
            {
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, contentType);
            }
            
            // 设置timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            
            // 发送请求
            var response = await _httpClient.SendAsync(request, 
                followRedirects ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead, 
                cts.Token);
            
            // 读取响应
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            var responseHeaders = response.Headers.ToDictionary(
                h => h.Key, 
                h => string.Join(", ", h.Value));
            
            // 尝试解析JSON响应
            object? parsedBody = responseBody;
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
            {
                try
                {
                    parsedBody = JsonSerializer.Deserialize<object>(responseBody);
                }
                catch
                {
                    // 保持为字符串
                }
            }
            
            logger?.LogInformation("HTTP request completed with status {StatusCode}", response.StatusCode);
            
            var resultObj = new
            {
                success = response.IsSuccessStatusCode,
                statusCode = (int)response.StatusCode,
                statusText = response.ReasonPhrase,
                headers = responseHeaders,
                body = parsedBody,
                requestInfo = new
                {
                    url = url,
                    method = method,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };
            
            var json = JsonSerializer.Serialize(resultObj);
            return JsonParser.Default.Parse<Struct>(json);
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "HTTP request failed");
            var errorResult = new { success = false, error = $"HTTP request failed: {ex.Message}" };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }
        catch (TaskCanceledException)
        {
            logger?.LogWarning("HTTP request timeout");
            var errorResult = new { success = false, error = "Request timeout" };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error in HTTP request");
            var errorResult = new { success = false, error = ex.Message };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }
    }
    
    /// <inheritdoc />
    public override ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        var result = base.ValidateParameters(parameters);
        
        // 验证URL
        if (parameters.TryGetValue("url", out var url))
        {
            var urlStr = url?.ToString();
            if (!Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
            {
                result.IsValid = false;
                result.Errors.Add("Invalid URL format");
            }
            else if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                result.IsValid = false;
                result.Errors.Add("Only HTTP and HTTPS URLs are supported");
            }
        }
        
        // 验证timeout
        if (parameters.TryGetValue("timeout", out var timeout))
        {
            var timeoutInt = Convert.ToInt32(timeout);
            if (timeoutInt < 5 || timeoutInt > 120)
            {
                result.Warnings.Add("Timeout value will be clamped to range 5-120 seconds");
            }
        }
        
        return result;
    }
    
    private static Dictionary<string, string>? ParseHeaders(object? headers)
    {
        if (headers == null) return null;
        
        if (headers is Dictionary<string, string> dict)
            return dict;
        
        if (headers is IDictionary<string, object> objDict)
        {
            return objDict.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? "");
        }
        
        // 尝试从JSON解析
        try
        {
            var json = JsonSerializer.Serialize(headers);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }
    
    private static TimeSpan ParseTimeout(object? value)
    {
        var seconds = Convert.ToInt32(value ?? 30);
        // 限制在 5-120 秒
        seconds = Math.Max(5, Math.Min(120, seconds));
        return TimeSpan.FromSeconds(seconds);
    }
    
    private static bool ParseBool(object? value)
    {
        if (value == null) return true;
        if (value is bool b) return b;
        return Convert.ToBoolean(value);
    }
}
