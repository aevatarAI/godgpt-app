using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Agents.GodGPT.Protos.Twitter;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Twitter OAuth2 authentication agent - Handles Twitter OAuth2 flow
/// Uses EventSourcing pattern for state management
/// </summary>
public class TwitterAuthGAgent : GAgentBase<TwitterAuthState>, ITwitterAuthGAgent
{
    private IHttpClientFactory? _httpClientFactory;
    private IOptionsMonitor<TwitterAuthOptions>? _authOptions;
    
    // Injected by OrleansGAgentGrain via reflection
    public IGAgentActorFactory? ActorFactory { get; set; }

    public TwitterAuthGAgent()
    {
    }

    /// <summary>
    /// HTTP client factory for making HTTP requests
    /// </summary>
    public IHttpClientFactory HttpClientFactory
    {
        set => _httpClientFactory = value;
    }

    /// <summary>
    /// Twitter auth options monitor
    /// </summary>
    public IOptionsMonitor<TwitterAuthOptions> AuthOptions
    {
        set => _authOptions = value;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Twitter OAuth2 Authentication GAgent");
    }

    /// <summary>
    /// Generate PKCE code verifier and challenge (plain method)
    /// </summary>
    public async Task<PkceResultProto> GeneratePkcePlainAsync()
    {
        var codeVerifier = "challenge";
        var codeChallenge = codeVerifier;
        
        RaiseEvent(new SetCodeVerifierEvent { CodeVerifier = codeVerifier });
        await ConfirmEventsAsync();
        
        return new PkceResultProto
        {
            CodeVerifier = codeVerifier,
            CodeChallenge = codeChallenge
        };
    }

    /// <summary>
    /// Generate PKCE code verifier and challenge (S256 method)
    /// </summary>
    public async Task<PkceResultProto> GeneratePkceAsync()
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        
        RaiseEvent(new SetCodeVerifierEvent { CodeVerifier = codeVerifier });
        await ConfirmEventsAsync();
        
        return new PkceResultProto
        {
            CodeVerifier = codeVerifier,
            CodeChallenge = codeChallenge
        };
    }

    /// <summary>
    /// Verify OAuth2 authorization code and bind Twitter account
    /// </summary>
    public async Task<TwitterAuthResultProto> VerifyAuthCodeAsync(string platform, string code, string redirectUri)
    {
        try
        {
            Logger.LogInformation("Starting OAuth verification with code: {CodeLength} chars", code?.Length ?? 0);
            
            // Exchange authorization code for access token
            var tokenResult = await ExchangeAuthCodeAsync(code ?? "", redirectUri);
            if (!tokenResult.Success)
            {
                Logger.LogError("Failed to exchange auth code for token: {Error}", tokenResult.Error);
                return new TwitterAuthResultProto { Success = false, Error = tokenResult.Error };
            }

            // Get Twitter user info
            var userInfo = await GetTwitterUserInfoAsync(tokenResult.AccessToken);
            if (!userInfo.Success)
            {
                Logger.LogError("Failed to get Twitter user info: {Error}", userInfo.Error);
                return new TwitterAuthResultProto { Success = false, Error = userInfo.Error };
            }

            // Raise TwitterAccountBoundEvent to update state via EventSourcing
            RaiseEvent(new TwitterAccountBoundEvent
            {
                UserId = Id.ToString(),
                TwitterId = userInfo.TwitterId,
                Username = userInfo.Username,
                AccessToken = tokenResult.AccessToken,
                RefreshToken = tokenResult.Scope,
                TokenExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn)),
                ProfileImageUrl = userInfo.ProfileImageUrl
            });
            await ConfirmEventsAsync();

            // Bind Twitter identity using actor factory
            if (ActorFactory != null)
            {
                var bindingActorId = CommonHelper.StringToGuid(userInfo.TwitterId).ToString();
                var bindingActor = await ActorFactory.CreateGAgentActorAsync<TwitterIdentityBindingGAgent>(bindingActorId);
                var bindingAgent = bindingActor.As<ITwitterIdentityBindingGAgent>();
                
                var bindingResult = await bindingAgent.CreateOrUpdateBindingAsync(
                    userInfo.TwitterId,
                    Guid.Parse(ExtractGuidFromId(Id.ToString())),
                    userInfo.Username,
                    userInfo.ProfileImageUrl);
                    
                if (!bindingResult.Success)
                {
                    Logger.LogError("Failed to bind Twitter identity: {Error}", bindingResult.Error);
                    return new TwitterAuthResultProto { Success = false, Error = bindingResult.Error };
                }
            }

            var redirectUrl = _authOptions?.CurrentValue?.PostLoginRedirectUrls?.GetValueOrDefault(platform, string.Empty) ?? string.Empty;
            
            return new TwitterAuthResultProto
            {
                Success = true,
                TwitterId = userInfo.TwitterId,
                Username = userInfo.Username,
                BindStatus = State.IsBound,
                ProfileImageUrl = userInfo.ProfileImageUrl,
                RedirectUri = redirectUrl
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error verifying auth code");
            return new TwitterAuthResultProto { Success = false, Error = "Internal server error" };
        }
    }

    /// <summary>
    /// Get Twitter binding status
    /// </summary>
    public Task<TwitterBindStatusProto> GetBindStatusAsync()
    {
        return Task.FromResult(new TwitterBindStatusProto
        {
            IsBound = State.IsBound,
            TwitterId = State.TwitterUserId ?? string.Empty,
            Username = State.Username ?? string.Empty,
            ProfileImageUrl = State.ProfileImageUrl ?? string.Empty
        });
    }

    /// <summary>
    /// Get OAuth2 authorization parameters
    /// </summary>
    public async Task<TwitterAuthParamsProto> GetAuthParamsAsync()
    {
        var pkceResult = await GeneratePkcePlainAsync();
        
        var scopes = _authOptions?.CurrentValue?.Scopes ?? Array.Empty<string>();
        
        return new TwitterAuthParamsProto
        {
            ClientId = _authOptions?.CurrentValue?.ClientId ?? string.Empty,
            GrantType = "authorization_code",
            CodeChallenge = pkceResult.CodeChallenge,
            CodeChallengeMethod = "plain",
            ResponseType = "code",
            Scope = string.Join(" ", scopes),
            State = Id.ToString()
        };
    }

    /// <summary>
    /// Handle state transitions for all events (EventSourcing pattern)
    /// </summary>
    protected override void TransitionState(TwitterAuthState state, IMessage @event)
    {
        switch (@event)
        {
            case SetCodeVerifierEvent codeEvent:
                state.CodeVerifier = codeEvent.CodeVerifier;
                break;
                
            case TwitterAccountBoundEvent boundEvent:
                state.UserId = boundEvent.UserId;
                state.TwitterUserId = boundEvent.TwitterId;
                state.Username = boundEvent.Username;
                state.AccessToken = boundEvent.AccessToken;
                state.RefreshToken = boundEvent.RefreshToken;
                state.TokenExpiresAt = boundEvent.TokenExpiresAt;
                state.ProfileImageUrl = boundEvent.ProfileImageUrl;
                state.IsBound = true;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", @event.GetType().Name);
                break;
        }
    }

    private async Task<TokenResult> ExchangeAuthCodeAsync(string code, string redirectUri)
    {
        try
        {
            if (_httpClientFactory == null || _authOptions == null)
            {
                return new TokenResult { Success = false, Error = "HTTP client or options not configured" };
            }

            var client = _httpClientFactory.CreateClient();
            var options = _authOptions.CurrentValue;

            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

            var formData = new Dictionary<string, string>
            {
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", redirectUri },
                { "code_verifier", "challenge" }
            };
            
            var response = await client.PostAsync(options.TokenEndpoint, new FormUrlEncodedContent(formData));
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("Token exchange failed: {Error}", responseContent);
                return new TokenResult { Success = false, Error = "Failed to exchange authorization code" };
            }

            var tokenResponse = JsonConvert.DeserializeObject<TwitterTokenResponse>(responseContent);
            if (tokenResponse == null)
            {
                return new TokenResult { Success = false, Error = "Invalid token response" };
            }

            return new TokenResult
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken ?? string.Empty,
                TokenType = tokenResponse.TokenType ?? string.Empty,
                ExpiresIn = tokenResponse.ExpiresIn,
                Scope = tokenResponse.Scope ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exchanging authorization code");
            return new TokenResult { Success = false, Error = "Internal server error" };
        }
    }

    private async Task<TwitterUserInfo> GetTwitterUserInfoAsync(string accessToken)
    {
        try
        {
            if (_httpClientFactory == null || _authOptions == null)
            {
                return new TwitterUserInfo { Success = false, Error = "HTTP client or options not configured" };
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync(_authOptions.CurrentValue.UserInfoEndpoint);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger.LogError("User info request failed: {Error}", error);
                return new TwitterUserInfo { Success = false, Error = "Failed to get user info" };
            }

            var content = await response.Content.ReadAsStringAsync();
            var userInfoResponse = JsonConvert.DeserializeObject<TwitterUserInfoResponse>(content);

            return new TwitterUserInfo
            {
                Success = true,
                TwitterId = userInfoResponse?.Data?.Id ?? string.Empty,
                Username = userInfoResponse?.Data?.Username ?? string.Empty,
                Name = userInfoResponse?.Data?.Name ?? string.Empty,
                ProfileImageUrl = userInfoResponse?.Data?.ProfileImageUrl ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting user information");
            return new TwitterUserInfo { Success = false, Error = "Internal server error" };
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ExtractGuidFromId(string id)
    {
        if (string.IsNullOrEmpty(id)) return Guid.Empty.ToString();
        
        var parts = id.Split(':');
        if (parts.Length >= 2)
        {
            return parts[^1];
        }
        return id;
    }
}

// Internal DTOs for token exchange
internal class TokenResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string Scope { get; set; } = string.Empty;
}

internal class TwitterTokenResponse
{
    [JsonProperty("access_token")]
    public string? AccessToken { get; set; }
    
    [JsonProperty("token_type")]
    public string? TokenType { get; set; }
    
    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonProperty("scope")]
    public string? Scope { get; set; }
}

internal class TwitterUserInfo
{
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public string TwitterId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfileImageUrl { get; set; } = string.Empty;
}

internal class TwitterUserInfoResponse
{
    [JsonProperty("data")]
    public TwitterUserData? Data { get; set; }
}

internal class TwitterUserData
{
    [JsonProperty("id")]
    public string? Id { get; set; }
    
    [JsonProperty("username")]
    public string? Username { get; set; }
    
    [JsonProperty("name")]
    public string? Name { get; set; }
    
    [JsonProperty("profile_image_url")]
    public string? ProfileImageUrl { get; set; }
}
