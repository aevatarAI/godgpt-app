using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Aevatar.Payment.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Aevatar.Payment.Providers;

/// <summary>
/// Apple App Store payment provider implementation
/// </summary>
public class ApplePayProvider : IPaymentProvider
{
    private readonly ILogger<ApplePayProvider> _logger;
    private readonly ApplePayOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentPlatform Platform => PaymentPlatform.AppStore;

    public ApplePayProvider(
        ILogger<ApplePayProvider> logger,
        IOptions<ApplePayOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("ApplePay");
    }

    public Task<List<ProductDto>> GetProductsAsync(CancellationToken ct = default)
    {
        // Apple products are configured in App Store Connect
        // Return configured products from options with originalPlanType metadata
        return Task.FromResult(_options.Products.Select(p => 
        {
            var billingCycle = p.GetBillingCycle();
            return new ProductDto
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Description = p.Description,
                Price = p.Amount,
                Currency = p.Currency,
                PlanType = p.IsUltimate ? PlanType.Premium : PlanType.Basic,
                BillingCycle = billingCycle,
                IsActive = true,
                Metadata = new Dictionary<string, string>
                {
                    ["originalPlanType"] = p.PlanType.ToString(),
                    ["isUltimate"] = p.IsUltimate.ToString().ToLower(),
                    ["dailyAvgPrice"] = CalculateDailyAvgPrice(p.Amount, billingCycle)
                }
            };
        }).ToList());
    }

    private static string CalculateDailyAvgPrice(decimal amount, BillingCycle cycle)
    {
        var days = cycle switch
        {
            BillingCycle.Daily => 1,
            BillingCycle.Weekly => 7,
            BillingCycle.Monthly => 30,
            BillingCycle.Quarterly => 90,
            BillingCycle.Yearly => 365,
            _ => 30
        };
        return Math.Round(amount / days, 2).ToString("F2");
    }

    public async Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest request, 
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ApplePayProvider] CreateSubscription - UserId={UserId}, TxId={TxId}, IsSandbox={IsSandbox}",
            request.UserId, request.TransactionId, request.IsSandbox);
        
        // Apple subscriptions are created via the app
        // Server-side just needs to verify the transaction
        if (string.IsNullOrEmpty(request.TransactionId))
        {
            _logger.LogWarning("[ApplePayProvider] CreateSubscription FAILED - TransactionId is empty");
            return new SubscriptionResult
            {
                Success = false,
                ErrorMessage = "TransactionId is required for App Store verification"
            };
        }

        var verification = await VerifyTransactionAsync(new VerificationRequest
        {
            UserId = request.UserId,
            TransactionId = request.TransactionId,
            IsSandbox = request.IsSandbox
        }, ct);

        if (!verification.IsValid)
        {
            _logger.LogWarning("[ApplePayProvider] CreateSubscription FAILED: {Error}", verification.ErrorMessage);
            return new SubscriptionResult
            {
                Success = false,
                ErrorMessage = verification.ErrorMessage
            };
        }

        _logger.LogInformation(
            "[ApplePayProvider] CreateSubscription SUCCESS - UserId={UserId}, OrderId={OrderId}, ProductId={ProductId}",
            request.UserId, verification.OriginalTransactionId, verification.ProductId);
        
        return new SubscriptionResult
        {
            Success = true,
            SubscriptionId = verification.OriginalTransactionId,
            OrderId = verification.OriginalTransactionId, // Required for RecordPaymentAsync
            ProductId = verification.ProductId, // Required for RecordPaymentAsync product lookup
            ExpiresAt = verification.ExpiresDate,
            Status = PaymentStatus.Completed
        };
    }

    public async Task<VerificationResult> VerifyTransactionAsync(
        VerificationRequest request, 
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ApplePayProvider] VerifyTransaction - TxId={TxId}, UserId={UserId}, IsSandbox={IsSandbox}",
            request.TransactionId, request.UserId, request.IsSandbox);
        
        try
        {
            var baseUrl = request.IsSandbox 
                ? "https://api.storekit-sandbox.itunes.apple.com"
                : "https://api.storekit.itunes.apple.com";

            var token = GenerateAppStoreToken();
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.GetAsync(
                $"{baseUrl}/inApps/v1/transactions/{request.TransactionId}", ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[ApplePayProvider] VerifyTransaction FAILED - TxId={TxId}, StatusCode={StatusCode}, Error={Error}",
                    request.TransactionId, response.StatusCode, errorContent);
                return new VerificationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Apple API returned {response.StatusCode}"
                };
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var transactionInfo = ParseSignedTransaction(content);

            _logger.LogInformation(
                "[ApplePayProvider] VerifyTransaction SUCCESS - TxId={TxId}, OrderId={OrderId}, ProductId={ProductId}, ExpiresDate={ExpiresDate}",
                transactionInfo.TransactionId, transactionInfo.OriginalTransactionId, transactionInfo.ProductId, transactionInfo.ExpiresDate);

            return new VerificationResult
            {
                IsValid = true,
                TransactionId = transactionInfo.TransactionId,
                OriginalTransactionId = transactionInfo.OriginalTransactionId,
                ProductId = transactionInfo.ProductId,
                PurchaseDate = transactionInfo.PurchaseDate,
                ExpiresDate = transactionInfo.ExpiresDate,
                AutoRenewing = transactionInfo.AutoRenewing
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ApplePayProvider] VerifyTransaction EXCEPTION - TxId={TxId}", request.TransactionId);
            return new VerificationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WebhookResult> HandleWebhookAsync(
        WebhookRequest request, 
        CancellationToken ct = default)
    {
        try
        {
            // Parse notification to get the signed payload
            var json = JsonDocument.Parse(request.Payload);
            var signedPayload = json.RootElement.GetProperty("signedPayload").GetString();
            
            // Verify JWT signature if enabled
            if (_options.EnableSignatureVerification && !string.IsNullOrEmpty(signedPayload))
            {
                if (!VerifyJwtSignature(signedPayload))
                {
                    _logger.LogWarning("[ApplePayProvider] JWT signature verification failed");
                    return new WebhookResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid JWT signature"
                    };
                }
            }
            
            var notification = ParseNotification(request.Payload);

            if (!IsAllowedNotificationType(notification.NotificationType, notification.Subtype))
            {
                return new WebhookResult
                {
                    Success = true,
                    ShouldProcess = false,
                    EventType = notification.NotificationType
                };
            }

            var transactionInfo = notification.TransactionInfo;
            // OrderId = OriginalTransactionId (required for ProcessWebhookResultAsync)
            var orderId = transactionInfo?.OriginalTransactionId;
            
            var result = new WebhookResult
            {
                Success = true,
                EventType = notification.NotificationType,
                TransactionId = transactionInfo?.TransactionId,
                SubscriptionId = transactionInfo?.OriginalTransactionId,
                OrderId = orderId,
                ShouldProcess = true
            };

            // Try to get user ID from app account token
            if (!string.IsNullOrEmpty(transactionInfo?.AppAccountToken) &&
                Guid.TryParse(transactionInfo.AppAccountToken, out var userId))
            {
                result.UserId = userId;
            }
            else
            {
                _logger.LogWarning("[ApplePayProvider] Webhook missing UserId - AppAccountToken={Token}", 
                    transactionInfo?.AppAccountToken);
            }
            
            result.NewStatus = MapAppleEventToStatus(notification.NotificationType, notification.Subtype);
            
            // Determine if this is a renewal - Apple uses "DID_RENEW" notification type
            result.IsRenewal = notification.NotificationType == "DID_RENEW";

            if (transactionInfo != null)
            {
                result.ProductId = transactionInfo.ProductId; // For product config lookup
                result.PeriodEnd = transactionInfo.ExpiresDate; // Set PeriodEnd for PaymentService
                result.VerificationResult = new VerificationResult
                {
                    IsValid = true,
                    TransactionId = transactionInfo.TransactionId,
                    OriginalTransactionId = transactionInfo.OriginalTransactionId,
                    ProductId = transactionInfo.ProductId,
                    PurchaseDate = transactionInfo.PurchaseDate,
                    ExpiresDate = transactionInfo.ExpiresDate,
                    AutoRenewing = transactionInfo.AutoRenewing,
                    Amount = transactionInfo.Price,
                    Currency = transactionInfo.Currency
                };
            }

            // Enhanced logging for refund events
            if (notification.NotificationType is "REFUND" or "REVOKE")
            {
                _logger.LogInformation(
                    "[ApplePayProvider] REFUND Webhook: Type={Type}, UserId={UserId}, OrderId={OrderId}, " +
                    "ProductId={ProductId}, RevocationDate={RevocationDate}, RevocationReason={RevocationReason}",
                    notification.NotificationType, result.UserId, orderId, 
                    transactionInfo?.ProductId, transactionInfo?.RevocationDate, transactionInfo?.RevocationReason);
            }
            else
            {
                _logger.LogInformation(
                    "[ApplePayProvider] Webhook: Type={Type}, Subtype={Subtype}, UserId={UserId}, OrderId={OrderId}, ProductId={ProductId}",
                    notification.NotificationType, notification.Subtype ?? "(none)", result.UserId, orderId, transactionInfo?.ProductId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ApplePayProvider] Webhook processing failed");
            return new WebhookResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public Task<CancellationResult> CancelSubscriptionAsync(
        CancellationRequest request, 
        CancellationToken ct = default)
    {
        // Apple subscriptions are cancelled via the App Store app
        // Server can only acknowledge the cancellation via webhook
        return Task.FromResult(new CancellationResult
        {
            Success = false,
            ErrorMessage = "App Store subscriptions must be cancelled through the App Store app"
        });
    }

    public async Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(
        string subscriptionId, 
        CancellationToken ct = default)
    {
        try
        {
            var token = GenerateAppStoreToken();
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            // Try production first, then sandbox
            var urls = new[]
            {
                $"https://api.storekit.itunes.apple.com/inApps/v1/subscriptions/{subscriptionId}",
                $"https://api.storekit-sandbox.itunes.apple.com/inApps/v1/subscriptions/{subscriptionId}"
            };

            foreach (var url in urls)
            {
                var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    var statusInfo = ParseSubscriptionStatus(content);
                    return statusInfo;
                }
            }

            return new SubscriptionStatusResult
            {
                IsActive = false,
                Status = PaymentStatus.Expired
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ApplePayProvider] Failed to get subscription status");
            return new SubscriptionStatusResult
            {
                IsActive = false,
                Status = PaymentStatus.Failed
            };
        }
    }

    #region Private Helpers

    /// <summary>
    /// Verifies the JWT signature using the x5c certificate chain from the JWT header.
    /// This validates that the notification came from Apple.
    /// </summary>
    private bool VerifyJwtSignature(string jwt)
    {
        try
        {
            _logger.LogDebug("[ApplePayProvider] Starting JWT signature verification");
            
            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                _logger.LogWarning("[ApplePayProvider] Invalid JWT format: does not have three parts");
                return false;
            }

            // Decode and parse the header
            var headerJson = DecodeBase64Url(parts[0]);
            var header = JsonDocument.Parse(headerJson);
            var headerRoot = header.RootElement;

            // Validate algorithm
            if (!headerRoot.TryGetProperty("alg", out var algElement) || 
                algElement.GetString() != "ES256")
            {
                _logger.LogWarning("[ApplePayProvider] Invalid or missing algorithm");
                return false;
            }

            // Extract x5c certificate chain
            if (!headerRoot.TryGetProperty("x5c", out var x5cElement) || 
                x5cElement.ValueKind != JsonValueKind.Array ||
                x5cElement.GetArrayLength() == 0)
            {
                _logger.LogWarning("[ApplePayProvider] Missing or invalid x5c certificate chain");
                return false;
            }

            // Convert certificate chain from base64 to certificates
            var certificateChain = new List<X509Certificate2>();
            foreach (var certElement in x5cElement.EnumerateArray())
            {
                try
                {
                    var certBase64 = certElement.GetString();
                    if (string.IsNullOrEmpty(certBase64)) continue;
                    
                    var certBytes = Convert.FromBase64String(certBase64);
#pragma warning disable SYSLIB0057 // X509Certificate2 constructor is obsolete
                    certificateChain.Add(new X509Certificate2(certBytes));
#pragma warning restore SYSLIB0057
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ApplePayProvider] Error decoding certificate from x5c chain");
                    return false;
                }
            }

            if (certificateChain.Count < 2)
            {
                _logger.LogWarning("[ApplePayProvider] Certificate chain too short: {Count}", certificateChain.Count);
                return false;
            }

            // Load Apple's root CA certificate if configured
            if (!string.IsNullOrEmpty(_options.AppleRootCertificatePath))
            {
                if (!File.Exists(_options.AppleRootCertificatePath))
                {
                    _logger.LogError("[ApplePayProvider] Apple Root CA certificate not found: {Path}", 
                        _options.AppleRootCertificatePath);
                    return false;
                }

#pragma warning disable SYSLIB0057 // X509Certificate2 constructor is obsolete
                var rootCert = new X509Certificate2(_options.AppleRootCertificatePath);
#pragma warning restore SYSLIB0057
                _logger.LogDebug("[ApplePayProvider] Loaded Apple Root CA: {Subject}", rootCert.Subject);

                // Build and validate the certificate chain
                var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(rootCert);

                var leafCert = certificateChain[0];
                var chainBuilt = chain.Build(leafCert);
                
                if (!chainBuilt)
                {
                    foreach (var element in chain.ChainElements)
                    {
                        foreach (var status in element.ChainElementStatus)
                        {
                            _logger.LogWarning("[ApplePayProvider] Chain validation error: {Status} - {Info}",
                                status.Status, status.StatusInformation);
                        }
                    }
                    return false;
                }

                _logger.LogDebug("[ApplePayProvider] Certificate chain validation successful");
            }

            // Extract public key from leaf certificate for JWT verification
            var publicKey = certificateChain[0].GetECDsaPublicKey();
            if (publicKey == null)
            {
                _logger.LogWarning("[ApplePayProvider] Failed to extract ECDsa public key");
                return false;
            }

            // Verify JWT signature
            var headerAndPayload = $"{parts[0]}.{parts[1]}";
            var signature = DecodeBase64UrlToBytes(parts[2]);
            var dataToVerify = System.Text.Encoding.UTF8.GetBytes(headerAndPayload);
            
            var isValid = publicKey.VerifyData(dataToVerify, signature, HashAlgorithmName.SHA256);

            if (isValid)
            {
                _logger.LogDebug("[ApplePayProvider] JWT signature verification successful");
            }
            else
            {
                _logger.LogWarning("[ApplePayProvider] JWT signature verification failed");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ApplePayProvider] Error during JWT signature verification");
            return false;
        }
    }

    private static byte[] DecodeBase64UrlToBytes(string input)
    {
        var base64 = input.Replace("-", "+").Replace("_", "/");
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    private string GenerateAppStoreToken()
    {
        var now = DateTime.UtcNow;
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(_options.PrivateKey), out _);

        var signingCredentials = new SigningCredentials(
            new ECDsaSecurityKey(key) { KeyId = _options.KeyId },
            SecurityAlgorithms.EcdsaSha256);

        var header = new JwtHeader(signingCredentials);
        header["alg"] = "ES256";
        header["kid"] = _options.KeyId;
        header["typ"] = "JWT";

        var claims = new JwtPayload
        {
            ["iss"] = _options.IssuerId,
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(now.AddMinutes(20)).ToUnixTimeSeconds(),
            ["aud"] = "appstoreconnect-v1",
            ["bid"] = _options.BundleId
        };

        var token = new JwtSecurityToken(header, claims);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private AppleTransactionInfo ParseSignedTransaction(string signedTransaction)
    {
        // Decode JWS transaction
        var parts = signedTransaction.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("Invalid signed transaction format");
        }

        var payload = DecodeBase64Url(parts[1]);
        var json = System.Text.Json.JsonDocument.Parse(payload);
        var root = json.RootElement;

        return new AppleTransactionInfo
        {
            TransactionId = root.GetProperty("transactionId").GetString() ?? string.Empty,
            OriginalTransactionId = root.GetProperty("originalTransactionId").GetString() ?? string.Empty,
            ProductId = root.GetProperty("productId").GetString() ?? string.Empty,
            PurchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(
                root.GetProperty("purchaseDate").GetInt64()).DateTime,
            ExpiresDate = root.TryGetProperty("expiresDate", out var exp) 
                ? DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()).DateTime 
                : null,
            AutoRenewing = !root.TryGetProperty("revocationDate", out _),
            AppAccountToken = root.TryGetProperty("appAccountToken", out var token) 
                ? token.GetString() 
                : null
        };
    }

    private AppleNotification ParseNotification(string payload)
    {
        var json = System.Text.Json.JsonDocument.Parse(payload);
        var root = json.RootElement;

        var signedPayload = root.GetProperty("signedPayload").GetString();
        var decodedPayload = DecodeJwsPayload(signedPayload!);

        var notificationJson = System.Text.Json.JsonDocument.Parse(decodedPayload);
        var notificationRoot = notificationJson.RootElement;

        var notification = new AppleNotification
        {
            NotificationType = notificationRoot.GetProperty("notificationType").GetString() ?? string.Empty,
            Subtype = notificationRoot.TryGetProperty("subtype", out var sub) ? sub.GetString() : null
        };

        if (notificationRoot.TryGetProperty("data", out var data) &&
            data.TryGetProperty("signedTransactionInfo", out var signedTx))
        {
            var txPayload = DecodeJwsPayload(signedTx.GetString()!);
            var txJson = System.Text.Json.JsonDocument.Parse(txPayload);
            var txRoot = txJson.RootElement;

            notification.TransactionInfo = new AppleTransactionInfo
            {
                TransactionId = txRoot.GetProperty("transactionId").GetString() ?? string.Empty,
                OriginalTransactionId = txRoot.GetProperty("originalTransactionId").GetString() ?? string.Empty,
                ProductId = txRoot.GetProperty("productId").GetString() ?? string.Empty,
                PurchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(
                    txRoot.GetProperty("purchaseDate").GetInt64()).DateTime,
                ExpiresDate = txRoot.TryGetProperty("expiresDate", out var exp)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()).DateTime
                    : null,
                AutoRenewing = !txRoot.TryGetProperty("revocationDate", out var revDate),
                AppAccountToken = txRoot.TryGetProperty("appAccountToken", out var token)
                    ? token.GetString()
                    : null,
                // Refund-related fields
                Price = txRoot.TryGetProperty("price", out var price) ? price.GetInt64() / 1000m : null,
                Currency = txRoot.TryGetProperty("currency", out var currency) ? currency.GetString() : null,
                RevocationDate = revDate.ValueKind != JsonValueKind.Undefined
                    ? DateTimeOffset.FromUnixTimeMilliseconds(revDate.GetInt64()).DateTime
                    : null,
                RevocationReason = txRoot.TryGetProperty("revocationReason", out var revReason)
                    ? revReason.GetInt32().ToString()
                    : null
            };
        }

        return notification;
    }

    private static string DecodeJwsPayload(string jws)
    {
        var parts = jws.Split('.');
        return DecodeBase64Url(parts[1]);
    }

    private static string DecodeBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static bool IsAllowedNotificationType(string type, string? subtype)
    {
        return type switch
        {
            // Subscription lifecycle events
            "SUBSCRIBED" => true,
            "DID_RENEW" => true,
            "DID_CHANGE_RENEWAL_STATUS" => true, // Both AUTO_RENEW_ENABLED and AUTO_RENEW_DISABLED
            "DID_CHANGE_RENEWAL_PREF" => true,   // UPGRADE/DOWNGRADE
            "EXPIRED" => true,
            "GRACE_PERIOD_EXPIRED" => true,
            
            // Refund events
            "REVOKE" => true,
            "REFUND" => true,
            "REFUND_REVERSED" => true,           // Reinstate subscription after refund reversal
            
            // Offer events
            "OFFER_REDEEMED" => true,            // May trigger upgrade
            
            // Skip events that don't need processing
            // TEST, DID_FAIL_TO_RENEW, RENEWAL_EXTENDED, PRICE_INCREASE, etc.
            _ => false
        };
    }

    private static PaymentStatus? MapAppleEventToStatus(string type, string? subtype)
    {
        return type switch
        {
            // Subscription active/renewed
            "SUBSCRIBED" => PaymentStatus.Completed,
            "DID_RENEW" => PaymentStatus.Completed,
            
            // Subscription ended
            "EXPIRED" => PaymentStatus.Expired,
            "GRACE_PERIOD_EXPIRED" => PaymentStatus.Expired,
            
            // Refund/revoke
            "REVOKE" => PaymentStatus.Refunded,
            "REFUND" => PaymentStatus.Refunded,
            
            // Refund reversed - reinstate subscription (same as old code)
            "REFUND_REVERSED" => PaymentStatus.Completed,
            
            // Auto-renewal status change
            "DID_CHANGE_RENEWAL_STATUS" when subtype == "AUTO_RENEW_DISABLED" => PaymentStatus.Cancelled,
            "DID_CHANGE_RENEWAL_STATUS" => null, // AUTO_RENEW_ENABLED - no status change, just log
            
            // Renewal preference change (plan upgrade/downgrade)
            "DID_CHANGE_RENEWAL_PREF" when subtype == "UPGRADE" => PaymentStatus.Completed,
            "DID_CHANGE_RENEWAL_PREF" => null, // DOWNGRADE - effective at next renewal
            
            // Offer redeemed
            "OFFER_REDEEMED" when subtype == "UPGRADE" => PaymentStatus.Completed,
            "OFFER_REDEEMED" => null, // Other offer types - no immediate status change
            
            // Events that don't change status
            "DID_FAIL_TO_RENEW" => null, // In grace period or billing retry
            "RENEWAL_EXTENDED" => null, // Just extends date, doesn't change status
            "PRICE_INCREASE" => null, // Pending customer consent
            "TEST" => null, // Test notification
            
            _ => PaymentStatus.Pending
        };
    }

    private SubscriptionStatusResult ParseSubscriptionStatus(string content)
    {
        // Parse subscription status from Apple API response
        var json = System.Text.Json.JsonDocument.Parse(content);
        var data = json.RootElement.GetProperty("data");
        
        if (data.GetArrayLength() == 0)
        {
            return new SubscriptionStatusResult { IsActive = false, Status = PaymentStatus.Expired };
        }

        var lastTransaction = data[0].GetProperty("lastTransactions")[0];
        var signedTx = lastTransaction.GetProperty("signedTransactionInfo").GetString();
        var txInfo = ParseSignedTransaction(signedTx!);

        return new SubscriptionStatusResult
        {
            IsActive = txInfo.ExpiresDate > DateTime.UtcNow,
            SubscriptionId = txInfo.OriginalTransactionId,
            Status = txInfo.ExpiresDate > DateTime.UtcNow ? PaymentStatus.Completed : PaymentStatus.Expired,
            CurrentPeriodEnd = txInfo.ExpiresDate,
            WillRenew = txInfo.AutoRenewing,
            ProductId = txInfo.ProductId
        };
    }

    #endregion

    #region Internal Types

    private class AppleTransactionInfo
    {
        public string TransactionId { get; set; } = string.Empty;
        public string OriginalTransactionId { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public DateTime? ExpiresDate { get; set; }
        public bool AutoRenewing { get; set; }
        public string? AppAccountToken { get; set; }
        // Refund-related fields
        public decimal? Price { get; set; }
        public string? Currency { get; set; }
        public DateTime? RevocationDate { get; set; }
        public string? RevocationReason { get; set; }
    }

    private class AppleNotification
    {
        public string NotificationType { get; set; } = string.Empty;
        public string? Subtype { get; set; }
        public AppleTransactionInfo? TransactionInfo { get; set; }
    }

    #endregion
}

/// <summary>
/// Apple Pay configuration options
/// </summary>
public class ApplePayOptions
{
    public const string SectionName = "ApplePay";
    
    public string IssuerId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string BundleId { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string SharedSecret { get; set; } = string.Empty;
    public List<AppleProductConfig> Products { get; set; } = new();
    
    /// <summary>
    /// Enable JWT signature verification for webhooks.
    /// Recommended for production environments.
    /// </summary>
    public bool EnableSignatureVerification { get; set; } = false;
    
    /// <summary>
    /// Path to Apple Root CA certificate (AppleRootCA-G3.cer).
    /// Required when EnableSignatureVerification is true.
    /// Download from: https://www.apple.com/certificateauthority/
    /// </summary>
    public string? AppleRootCertificatePath { get; set; }
}

public class AppleProductConfig
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Original plan type value from config (1=Day, 2=Month, 3=Year, 4=Week).
    /// This matches the legacy GodGPT PlanType enum values.
    /// </summary>
    public int PlanType { get; set; }
    public bool IsUltimate { get; set; }
    
    /// <summary>
    /// Maps the legacy PlanType value to BillingCycle for internal calculations.
    /// Legacy PlanType: 1=Day, 2=Month, 3=Year, 4=Week
    /// BillingCycle: 1=Daily, 2=Weekly, 3=Monthly, 4=Quarterly, 5=Yearly
    /// </summary>
    public BillingCycle GetBillingCycle() => PlanType switch
    {
        1 => BillingCycle.Daily,    // Day -> Daily
        2 => BillingCycle.Monthly,  // Month -> Monthly
        3 => BillingCycle.Yearly,   // Year -> Yearly
        4 => BillingCycle.Weekly,   // Week -> Weekly
        _ => BillingCycle.Monthly   // Default to Monthly
    };
}

