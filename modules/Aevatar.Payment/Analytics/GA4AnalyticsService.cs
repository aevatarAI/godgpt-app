using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aevatar.Payment.Agents;
using Aevatar.Payment.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Payment.Analytics;

/// <summary>
/// Google Analytics 4 implementation of payment analytics service
/// Uses GA4 Measurement Protocol for server-side event tracking
/// </summary>
public class GA4AnalyticsService : IPaymentAnalyticsService
{
    private readonly ILogger<GA4AnalyticsService> _logger;
    private readonly IOptionsMonitor<GA4Options> _options;
    private readonly HttpClient _httpClient;

    public GA4AnalyticsService(
        ILogger<GA4AnalyticsService> logger,
        IOptionsMonitor<GA4Options> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options;
        _httpClient = httpClient;
    }

    public async Task<bool> ReportPurchaseAsync(
        string platform,
        string transactionId,
        string userId,
        decimal amount,
        string currency,
        bool isRenewal = false,
        CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        
        if (!options.EnableAnalytics)
        {
            _logger.LogDebug("[GA4] Analytics disabled, skipping purchase report");
            return true;
        }

        // Create unique transaction ID for GA4 deduplication
        var ga4TransactionId = $"{userId}^{platform}^{transactionId}";
        
        var payload = new
        {
            client_id = ga4TransactionId,
            events = new[]
            {
                new
                {
                    name = "purchase",
                    @params = new Dictionary<string, object>
                    {
                        ["transaction_id"] = ga4TransactionId,
                        ["value"] = amount,
                        ["currency"] = currency,
                        ["payment_type"] = platform,
                        ["is_renewal"] = isRenewal
                    }
                }
            }
        };

        return await SendWithRetryAsync(payload, "purchase", ga4TransactionId, options, ct);
    }

    public async Task<bool> ReportRefundAsync(
        string platform,
        string transactionId,
        string userId,
        decimal refundAmount,
        string currency,
        string reason,
        CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        
        if (!options.EnableAnalytics)
        {
            _logger.LogDebug("[GA4] Analytics disabled, skipping refund report");
            return true;
        }

        var ga4TransactionId = $"{userId}^{platform}^{transactionId}";
        
        var payload = new
        {
            client_id = ga4TransactionId,
            events = new[]
            {
                new
                {
                    name = "refund",
                    @params = new Dictionary<string, object>
                    {
                        ["transaction_id"] = ga4TransactionId,
                        ["value"] = refundAmount,
                        ["currency"] = currency,
                        ["refund_reason"] = reason
                    }
                }
            }
        };

        return await SendWithRetryAsync(payload, "refund", ga4TransactionId, options, ct);
    }

    private async Task<bool> SendWithRetryAsync(
        object payload,
        string eventType,
        string transactionId,
        GA4Options options,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.MeasurementId) || string.IsNullOrEmpty(options.ApiSecret))
        {
            _logger.LogWarning("[GA4] Missing MeasurementId or ApiSecret, skipping report");
            return false;
        }

        var url = $"{options.ApiEndpoint}?measurement_id={options.MeasurementId}&api_secret={options.ApiSecret}";
        var jsonPayload = JsonSerializer.Serialize(payload);
        
        for (int attempt = 1; attempt <= options.RetryCount; attempt++)
        {
            try
            {
                _logger.LogDebug("[GA4] Sending {EventType} event (attempt {Attempt}/{MaxAttempts}): {TransactionId}",
                    eventType, attempt, options.RetryCount, transactionId);

                using var content = new StringContent(jsonPayload, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

                var response = await _httpClient.PostAsync(url, content, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[GA4] Successfully reported {EventType} for {TransactionId}",
                        eventType, transactionId);
                    return true;
                }

                // 4xx errors - don't retry (client error)
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("[GA4] Client error {StatusCode} for {EventType}, not retrying: {Error}",
                        response.StatusCode, eventType, errorContent);
                    return false;
                }

                // 5xx errors - retry
                _logger.LogWarning("[GA4] Server error {StatusCode} on attempt {Attempt} for {EventType}",
                    response.StatusCode, attempt, eventType);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("[GA4] Request cancelled for {EventType}", eventType);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[GA4] Exception on attempt {Attempt} for {EventType}",
                    attempt, eventType);
            }

            // Wait before retry
            if (attempt < options.RetryCount)
            {
                await Task.Delay(options.RetryDelayMs * attempt, ct);
            }
        }

        _logger.LogError("[GA4] Failed to report {EventType} after {MaxAttempts} attempts: {TransactionId}",
            eventType, options.RetryCount, transactionId);
        return false;
    }
}
