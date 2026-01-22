using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Aevatar.App.Application.Services;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.HttpApi.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Proxy controller for backward compatibility with old /api/account routes.
/// Forwards requests to AuthServer's /api/app/account endpoints.
/// This enables existing frontend clients to continue using the old URL format.
/// </summary>
[RemoteService]
[Route("api/account")]
public class AccountProxyController : AevatarController
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AccountProxyController> _logger;
    private readonly AuthServerProxyOptions _options;
    private readonly IIpLocationService _ipLocationService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AccountProxyController(
        IHttpClientFactory httpClientFactory,
        IOptions<AuthServerProxyOptions> options,
        IIpLocationService ipLocationService,
        ILogger<AccountProxyController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _ipLocationService = ipLocationService;
        _logger = logger;
    }

    /// <summary>
    /// Send registration verification code (backward compatible)
    /// </summary>
    [HttpPost("send-register-code")]
    public async Task<IActionResult> SendRegisterCodeAsync([FromBody] JsonElement input)
    {
        return await ProxyToAuthServerAsync("send-register-code", input);
    }

    /// <summary>
    /// Register user (backward compatible)
    /// Adds default AppName if not provided.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync([FromBody] JsonElement input)
    {
        // Ensure AppName is present (old DTOs may not have it)
        var body = EnsureAppName(input);
        return await ProxyToAuthServerAsync("register", body);
    }

    /// <summary>
    /// GodGPT specific registration (backward compatible)
    /// Maps to standard register endpoint with AppName="GodGPT"
    /// </summary>
    [HttpPost("godgpt-register")]
    public async Task<IActionResult> GodgptRegisterAsync([FromBody] JsonElement input)
    {
        // Map to register with AppName="GodGPT"
        var body = EnsureAppName(input, "GodGPT");
        return await ProxyToAuthServerAsync("register", body);
    }

    /// <summary>
    /// Verify registration code (backward compatible)
    /// </summary>
    [HttpPost("verify-register-code")]
    public async Task<IActionResult> VerifyRegisterCodeAsync([FromBody] JsonElement input)
    {
        return await ProxyToAuthServerAsync("verify-register-code", input);
    }

    /// <summary>
    /// Check if email is registered (backward compatible)
    /// </summary>
    [HttpPost("check-email-registered")]
    public async Task<IActionResult> CheckEmailRegisteredAsync([FromBody] JsonElement input)
    {
        return await ProxyToAuthServerAsync("check-email-registered", input);
    }

    /// <summary>
    /// Send password reset code (backward compatible)
    /// Includes IP location detection for CN-specific reset URLs
    /// Note: Route order 0 to take precedence over ABP's AccountController
    /// </summary>
    [HttpPost("send-password-reset-code")]
    public async Task<IActionResult> SendPasswordResetCodeAsync([FromBody] JsonElement input)
    {
        // Detect if user is from mainland China for URL selection
        var clientIp = HttpContext.GetClientIpAddress();
        var appType = HttpContext.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
        
        return await ProxyToAuthServerAsync("send-password-reset-code", input, isCN);
    }

    /// <summary>
    /// Verify password reset token (backward compatible)
    /// </summary>
    [HttpPost("verify-password-reset-token")]
    public async Task<IActionResult> VerifyPasswordResetTokenAsync([FromBody] JsonElement input)
    {
        return await ProxyToAuthServerAsync("verify-password-reset-token", input);
    }

    /// <summary>
    /// Reset password (backward compatible)
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] JsonElement input)
    {
        return await ProxyToAuthServerAsync("reset-password", input);
    }

    /// <summary>
    /// Forward request to AuthServer
    /// </summary>
    /// <param name="endpoint">API endpoint name</param>
    /// <param name="body">Request body as JSON</param>
    /// <param name="isCN">Optional: whether user is from mainland China (for URL selection)</param>
    private async Task<IActionResult> ProxyToAuthServerAsync(string endpoint, JsonElement body, bool? isCN = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AuthServer");
            var targetUrl = $"{_options.BaseUrl}/api/app/account/{endpoint}";

            _logger.LogInformation(
                "[AccountProxy] Forwarding to AuthServer: {Endpoint} -> {TargetUrl}",
                endpoint, targetUrl);

            // Prepare request
            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
            };

            // Forward important headers
            ForwardHeaders(request, isCN);

            // Send request
            var response = await client.SendAsync(request);

            // Read response content
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Parse and return as object so AutoResponseWrapper can wrap it
                // This matches old API format: {code, data, message}
                if (string.IsNullOrWhiteSpace(content))
                {
                    return Ok();
                }
                
                try
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(content);
                    return Ok(result);
                }
                catch (JsonException)
                {
                    return Content(content, "application/json");
                }
            }

            // Log error and return appropriate status
            _logger.LogWarning(
                "[AccountProxy] AuthServer returned {StatusCode} for {Endpoint}: {Content}",
                (int)response.StatusCode, endpoint, content);

            // Try to deserialize error response, but handle empty/invalid JSON gracefully
            object? errorResponse = null;
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    errorResponse = JsonSerializer.Deserialize<object>(content);
                }
                catch (JsonException)
                {
                    // If content is not valid JSON, return as plain text
                    _logger.LogWarning("[AccountProxy] Error response is not valid JSON, returning as plain text");
                    return StatusCode((int)response.StatusCode, content);
                }
            }

            return StatusCode((int)response.StatusCode, errorResponse ?? new { error = "Request failed" });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[AccountProxy] Network error calling AuthServer for {Endpoint}", endpoint);
            throw new UserFriendlyException("Authentication service temporarily unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AccountProxy] Error proxying request to AuthServer for {Endpoint}", endpoint);
            throw;
        }
    }

    /// <summary>
    /// Forward relevant headers to AuthServer
    /// </summary>
    /// <param name="request">HTTP request message</param>
    /// <param name="isCN">Optional: whether user is from mainland China</param>
    private void ForwardHeaders(HttpRequestMessage request, bool? isCN = null)
    {
        // Forward language header
        if (HttpContext.Request.Headers.TryGetValue("GodGPTLanguage", out var language))
        {
            request.Headers.TryAddWithoutValidation("GodGPTLanguage", language.ToString());
        }

        // Forward authorization header if present
        if (HttpContext.Request.Headers.TryGetValue("Authorization", out var auth))
        {
            request.Headers.TryAddWithoutValidation("Authorization", auth.ToString());
        }

        // Forward X-Forwarded headers for proper IP detection
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(clientIp))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        }
        
        // Forward CN location flag if provided (for password reset URL selection)
        if (isCN.HasValue)
        {
            request.Headers.TryAddWithoutValidation("X-Is-CN", isCN.Value.ToString().ToLower());
        }
    }

    /// <summary>
    /// Ensure AppName exists in the request body
    /// </summary>
    private static JsonElement EnsureAppName(JsonElement input, string defaultAppName = "GodGPT")
    {
        using var doc = JsonDocument.Parse(input.GetRawText());
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(input.GetRawText(), JsonOptions)
            ?? new Dictionary<string, object>();

        // Add AppName if not present
        if (!dict.ContainsKey("appName") && !dict.ContainsKey("AppName"))
        {
            dict["appName"] = defaultAppName;
        }

        var newJson = JsonSerializer.Serialize(dict, JsonOptions);
        return JsonDocument.Parse(newJson).RootElement.Clone();
    }
}

/// <summary>
/// Configuration options for AuthServer proxy
/// </summary>
public class AuthServerProxyOptions
{
    /// <summary>
    /// Base URL of AuthServer (e.g., "http://localhost:8001")
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8001";
}
