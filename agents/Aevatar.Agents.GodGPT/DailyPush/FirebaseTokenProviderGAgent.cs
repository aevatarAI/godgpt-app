using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.DailyPush;
using GodGPT.GAgents.DailyPush.Options;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Firebase access token provider GAgent implementation
/// Each ChatManager has its own instance - eliminates concurrency issues with JWT creation
/// </summary>
[GAgent(nameof(FirebaseTokenProviderGAgent))]
public class FirebaseTokenProviderGAgent : GAgentBase<FirebaseTokenProviderState>, 
    IFirebaseTokenProviderGAgent
{
    private readonly IOptionsMonitor<DailyPushOptions> _options;
    private readonly HttpClient _httpClient;

    public FirebaseTokenProviderGAgent(
        Guid id,
        IOptionsMonitor<DailyPushOptions> options,
        HttpClient httpClient) : base(id)
    {
        _options = options;
        _httpClient = httpClient;
    }
    
    // State helper methods (moved from State class)
    private bool IsTokenValid(int bufferMinutes = 1)
    {
        return !string.IsNullOrEmpty(State.CachedAccessToken) && 
               DateTime.UtcNow < State.TokenExpiry.ToDateTime().AddMinutes(-bufferMinutes);
    }
    
    private void ClearToken()
    {
        State.ClearCachedAccessToken();
        State.TokenExpiry = Timestamp.FromDateTime(DateTime.MinValue.ToUniversalTime());
    }
    
    private void UpdateToken(string token, DateTime expiry)
    {
        State.CachedAccessToken = token;
        State.TokenExpiry = Timestamp.FromDateTime(expiry.ToUniversalTime());
        State.LastSuccessTime = Timestamp.FromDateTime(DateTime.UtcNow);
        State.SuccessfulCreations++;
    }
    
    private void RecordFailure(string error)
    {
        State.FailedAttempts++;
        State.LastError = error;
    }
    
    private void IncrementRequests()
    {
        State.TotalRequests++;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Firebase token provider for ChatManager {Id}");
    }

    protected override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        await base.OnActivateAsync(cancellationToken);
        
        Logger.LogInformation("FirebaseTokenProviderGAgent activated for ChatManager {ChatManagerId}", Id);
        State.ActivationTime = Timestamp.FromDateTime(DateTime.UtcNow);
    }



    public async Task<string?> GetAccessTokenAsync()
    {
        IncrementRequests();

        // Check if cached token is still valid
        if (IsTokenValid())
        {
            Logger.LogDebug("Using cached access token for ChatManager {ChatManagerId}", Id);
            return State.CachedAccessToken;
        }

        // Create token directly in this Agent instance (no shared state, no concurrency issues)
        return await CreateAccessTokenAsync();
    }

    /// <summary>
    /// Create access token directly in this Agent instance - eliminates concurrency issues
    /// Each ChatManager has its own FirebaseTokenProviderGAgent, so no shared state
    /// </summary>
    private async Task<string?> CreateAccessTokenAsync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 1000;

        var firebaseKeyPath = _options?.CurrentValue?.FilePaths?.FirebaseKeyPath;
        if (string.IsNullOrEmpty(firebaseKeyPath))
        {
            var configError = "❌ CRITICAL CONFIG ERROR: DailyPushOptions.FilePaths.FirebaseKeyPath not configured - falling back to legacy with RSA issues";
            Logger.LogError("{Error} for ChatManager {ChatManagerId}", configError, Id);
            RecordFailure(configError);
            return null;
        }

        if (_httpClient == null)
        {
            var httpError = "HttpClient not available";
            Logger.LogError("{Error} for ChatManager {ChatManagerId}", httpError, Id);
            RecordFailure(httpError);
            return null;
        }

        // Load service account from Firebase key file
        var serviceAccount = LoadServiceAccountFromFile(firebaseKeyPath);

        if (serviceAccount == null)
        {
            var loadError = $"Failed to load service account from {firebaseKeyPath}";
            Logger.LogError("{Error} for ChatManager {ChatManagerId}", loadError, Id);
            RecordFailure(loadError);
            return null;
        }

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Create JWT for OAuth 2.0 flow
                var now = DateTimeOffset.UtcNow;
                var expiry = now.AddHours(1); // 1 hour expiry per agent instance

                var claims = new Dictionary<string, object>
                {
                    ["iss"] = serviceAccount.ClientEmail,
                    ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
                    ["aud"] = "https://oauth2.googleapis.com/token",
                    ["iat"] = now.ToUnixTimeSeconds(),
                    ["exp"] = expiry.ToUnixTimeSeconds()
                };

                // Create JWT using same pattern as UserBillingGrain
                var jwt = CreateJwt(claims, serviceAccount.PrivateKey);
                if (string.IsNullOrEmpty(jwt))
                {
                    var error = $"Failed to create JWT on attempt {attempt}";
                    Logger.LogError("{Error} for ChatManager {ChatManagerId}", error, Id);
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs * attempt);
                        continue;
                    }
                    
                    RecordFailure(error);
                    return null;
                }

                // Exchange JWT for access token
                var tokenRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new KeyValuePair<string, string>("assertion", jwt)
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequest, cts.Token);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
                    if (tokenResponse?.AccessToken != null)
                    {
                        // Cache token locally with 1-hour expiry
                        var tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60); // 1 min buffer
                        UpdateToken(tokenResponse.AccessToken, tokenExpiry);
                        
                        Logger.LogInformation("Successfully created access token for ChatManager {ChatManagerId} on attempt {Attempt}", 
                            Id, attempt);
                        
                        return tokenResponse.AccessToken;
                    }
                }

                var httpError = $"Failed to obtain access token on attempt {attempt}/{maxRetries}: {response.StatusCode} - {responseContent}";
                Logger.LogWarning("{Error} for ChatManager {ChatManagerId}", httpError, Id);

                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt; // Exponential backoff
                    await Task.Delay(delay);
                    continue;
                }

                RecordFailure(httpError);
                return null;
            }
            catch (Exception ex)
            {
                var exceptionError = $"Exception during token creation attempt {attempt}: {ex.Message}";
                Logger.LogError(ex, "{Error} for ChatManager {ChatManagerId}", exceptionError, Id);
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelayMs * attempt);
                    continue;
                }
                
                RecordFailure(exceptionError);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Load service account information from Firebase key file
    /// </summary>
    private ServiceAccountInfo? LoadServiceAccountFromFile(string firebaseKeyPath)
    {
        try
        {
            Logger.LogDebug("Loading Firebase key from path: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, Id);

            if (!File.Exists(firebaseKeyPath))
            {
                Logger.LogWarning("Firebase key file not found: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, Id);
                return null;
            }

            var jsonContent = File.ReadAllText(firebaseKeyPath);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                Logger.LogWarning("Firebase key file is empty: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, Id);
                return null;
            }

            var serviceAccount = JsonSerializer.Deserialize<ServiceAccountInfo>(jsonContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            if (serviceAccount != null)
            {
                Logger.LogDebug("Successfully loaded Firebase service account for ChatManager {ChatManagerId}", Id);
                return serviceAccount;
            }

            Logger.LogWarning("Failed to deserialize Firebase service account from file: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, Id);
            return null;
        }
        catch (JsonException jsonEx)
        {
            Logger.LogError(jsonEx, "JSON parsing error loading Firebase service account from file: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, Id);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Critical error loading Firebase service account from file: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, Id);
            return null;
        }
    }

    /// <summary>
    /// Create JWT using RSA private key - same pattern as UserBillingGrain to avoid ObjectDisposedException
    /// Each Agent instance handles its own JWT creation - no concurrency issues
    /// </summary>
    private string? CreateJwt(Dictionary<string, object> claims, string privateKeyPem)
    {
        try
        {
            // Clean up the private key format
            var privateKeyContent = privateKeyPem
                .Replace("-----BEGIN PRIVATE KEY-----", "")
                .Replace("-----END PRIVATE KEY-----", "")
                .Replace("\\n", "\n")
                .Replace("\n", "")
                .Replace("\r", "")
                .Trim();

            var privateKeyBytes = Convert.FromBase64String(privateKeyContent);

            // Simple RSA lifecycle management - complete all JWT operations within using block
            using (var rsa = RSA.Create())
            {
                rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                
                // Create security key and signing credentials
                var securityKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString() };
                var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

                // Create token descriptor and sign token within RSA lifetime
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Claims = claims,
                    SigningCredentials = signingCredentials
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
                
                // Return signed token - all operations completed within RSA scope
                return tokenHandler.WriteToken(token);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating JWT for ChatManager {ChatManagerId}: {ErrorMessage}", 
                Id, ex.Message);
            return null;
        }
    }

    public async Task<bool> IsTokenValidAsync()
    {
        return IsTokenValid();
    }

    public async Task ClearTokenCacheAsync()
    {
        Logger.LogInformation("Clearing token cache for ChatManager {ChatManagerId}", Id);
        ClearToken();
    }

    public async Task<TokenProviderStatus> GetStatusAsync()
    {
        return new TokenProviderStatus
        {
            IsReady = !string.IsNullOrEmpty(_options?.CurrentValue?.FilePaths?.FirebaseKeyPath), // Ready if firebase key path is configured
            HasCachedToken = !string.IsNullOrEmpty(State.CachedAccessToken),
            TokenExpiry = State.TokenExpiry?.ToDateTime(),
            TotalRequests = State.TotalRequests,
            SuccessfulCreations = State.SuccessfulCreations,
            FailedAttempts = State.FailedAttempts,
            LastSuccessTime = State.LastSuccessTime != null ? State.LastSuccessTime.ToDateTime() : (DateTime?)null,
            LastError = State.LastError
        };
    }
}
