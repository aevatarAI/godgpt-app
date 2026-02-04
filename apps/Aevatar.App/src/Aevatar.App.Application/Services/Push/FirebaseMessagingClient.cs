using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Aevatar.App.Application.Services.Push;

/// <summary>
/// Firebase Cloud Messaging client using HTTP v1 API.
/// </summary>
public class FirebaseMessagingClient : IFirebaseMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly FirebaseMessagingOptions _options;
    private readonly ILogger<FirebaseMessagingClient> _logger;
    
    private string? _accessToken;
    private DateTime _tokenExpiry;
    private ServiceAccountInfo? _serviceAccount;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public FirebaseMessagingClient(
        HttpClient httpClient,
        IOptions<FirebaseMessagingOptions> options,
        ILogger<FirebaseMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<SendResult> SendAsync(string token, PushMessage message, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("[FirebaseMessagingClient] Firebase messaging is disabled");
            return new SendResult { Success = false, Error = "Firebase messaging is disabled" };
        }
        
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            
            var requestBody = new
            {
                message = new
                {
                    token = token,
                    notification = new
                    {
                        title = message.Title,
                        body = message.Body
                    },
                    data = message.Data,
                    android = new
                    {
                        priority = "high",
                        notification = new
                        {
                            sound = "default",
                            channel_id = "daily_push_channel"
                        }
                    },
                    apns = new
                    {
                        headers = new
                        {
                            apns_push_type = "alert"
                        },
                        payload = new
                        {
                            aps = new
                            {
                                sound = "default"
                            }
                        }
                    }
                }
            };
            
            var endpoint = string.Format(_options.ApiEndpoint, _options.ProjectId);
            
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");
            
            var response = await _httpClient.SendAsync(request, ct);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FcmResponse>(JsonOptions, ct);
                return new SendResult { Success = true, MessageId = result?.Name };
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                var isTokenInvalid = IsTokenInvalidError(errorContent);
                
                _logger.LogWarning("[FirebaseMessagingClient] Failed to send: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                
                return new SendResult 
                { 
                    Success = false, 
                    Error = errorContent,
                    TokenInvalid = isTokenInvalid
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FirebaseMessagingClient] Exception while sending push notification");
            return new SendResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<BatchSendResult> SendBatchAsync(IEnumerable<string> tokens, PushMessage message, CancellationToken ct = default)
    {
        var result = new BatchSendResult();
        var tokenList = tokens.ToList();
        
        if (tokenList.Count == 0)
        {
            return result;
        }
        
        var semaphore = new SemaphoreSlim(_options.MaxConcurrency);
        
        var tasks = tokenList.Select(async token =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var sendResult = await SendAsync(token, message, ct);
                return (token, sendResult);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        var results = await Task.WhenAll(tasks);
        
        foreach (var (token, sendResult) in results)
        {
            if (sendResult.Success)
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
                result.FailedTokens.Add((token, sendResult.Error, sendResult.TokenInvalid));
            }
        }
        
        _logger.LogInformation("[FirebaseMessagingClient] Batch send completed: {Success}/{Total} succeeded", 
            result.SuccessCount, tokenList.Count);
        
        return result;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
        {
            return _accessToken;
        }
        
        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }
            
            var serviceAccount = GetServiceAccount();
            var jwt = CreateJwt(serviceAccount);
            var tokenResponse = await ExchangeJwtForAccessTokenAsync(jwt, ct);
            
            _accessToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
            
            _logger.LogDebug("[FirebaseMessagingClient] Obtained new access token, expires at {Expiry}", _tokenExpiry);
            
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private ServiceAccountInfo GetServiceAccount()
    {
        if (_serviceAccount != null)
        {
            return _serviceAccount;
        }
        
        string json;
        
        if (!string.IsNullOrEmpty(_options.ServiceAccountJsonPath) && File.Exists(_options.ServiceAccountJsonPath))
        {
            json = File.ReadAllText(_options.ServiceAccountJsonPath);
        }
        else if (!string.IsNullOrEmpty(_options.ServiceAccountJson))
        {
            json = _options.ServiceAccountJson;
        }
        else
        {
            throw new InvalidOperationException("Firebase service account JSON not configured");
        }
        
        _serviceAccount = JsonSerializer.Deserialize<ServiceAccountInfo>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse service account JSON");
        
        return _serviceAccount;
    }

    private static string CreateJwt(ServiceAccountInfo serviceAccount)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim("iss", serviceAccount.ClientEmail),
            new Claim("sub", serviceAccount.ClientEmail),
            new Claim("aud", "https://oauth2.googleapis.com/token"),
            new Claim("iat", ((DateTimeOffset)now).ToUnixTimeSeconds().ToString()),
            new Claim("exp", ((DateTimeOffset)now.AddHours(1)).ToUnixTimeSeconds().ToString()),
            new Claim("scope", "https://www.googleapis.com/auth/firebase.messaging")
        };
        
        using var rsa = RSA.Create();
        rsa.ImportFromPem(serviceAccount.PrivateKey);
        
        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(rsa) { KeyId = serviceAccount.PrivateKeyId },
            SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        
        var token = new JwtSecurityToken(
            claims: claims,
            signingCredentials: signingCredentials);
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<TokenResponse> ExchangeJwtForAccessTokenAsync(string jwt, CancellationToken ct)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt
        });
        
        var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content, ct);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse token response");
    }

    private static bool IsTokenInvalidError(string errorContent)
    {
        // FCM returns specific error codes for invalid tokens
        return errorContent.Contains("UNREGISTERED") ||
               errorContent.Contains("INVALID_ARGUMENT") ||
               errorContent.Contains("NOT_FOUND");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal class ServiceAccountInfo
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;
    
    [JsonPropertyName("private_key_id")]
    public string PrivateKeyId { get; set; } = string.Empty;
    
    [JsonPropertyName("private_key")]
    public string PrivateKey { get; set; } = string.Empty;
    
    [JsonPropertyName("client_email")]
    public string ClientEmail { get; set; } = string.Empty;
    
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;
}

internal class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}

internal class FcmResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
