using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Analytics;
using Aevatar.App.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Volo.Abp.DependencyInjection;

namespace Aevatar.App.Application.Services;

/// <summary>
/// Google Analytics event tracking service interface
/// </summary>
public interface IGoogleAnalyticsService
{
    /// <summary>
    /// Track event to Google Analytics
    /// </summary>
    Task<GoogleAnalyticsEventResponseDto> TrackEventAsync(GoogleAnalyticsEventRequestDto eventRequest);
    
    /// <summary>
    /// Track event to Firebase Analytics
    /// </summary>
    Task<GoogleAnalyticsEventResponseDto> TrackFirebaseEventAsync(GoogleAnalyticsEventRequestDto eventRequest);
    
    /// <summary>
    /// Track multiple events to Firebase Analytics in a single batch request
    /// </summary>
    Task<GoogleAnalyticsBatchEventResponseDto> TrackFirebaseBatchEventsAsync(GoogleAnalyticsBatchEventRequestDto batchRequest);
} 

/// <summary>
/// Google Analytics event tracking service implementation
/// </summary>
public class GoogleAnalyticsService : IGoogleAnalyticsService, ITransientDependency
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GoogleAnalyticsOptions _options;
    private readonly FirebaseAnalyticsOptions _firebaseOptions;
    private readonly ILogger<GoogleAnalyticsService> _logger;

    public GoogleAnalyticsService(
        IHttpClientFactory httpClientFactory,
        IOptions<GoogleAnalyticsOptions> options,
        IOptions<FirebaseAnalyticsOptions> firebaseOptions,
        ILogger<GoogleAnalyticsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _firebaseOptions = firebaseOptions.Value;
        _logger = logger;
    }

    public async Task<GoogleAnalyticsEventResponseDto> TrackEventAsync(GoogleAnalyticsEventRequestDto eventRequest)
    {
        try
        {
            if (!_options.EnableAnalytics)
            {
                _logger.LogDebug("Analytics reporting is disabled in configuration");
                return new GoogleAnalyticsEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "Analytics reporting is disabled"
                };
            }
            
            if (string.IsNullOrWhiteSpace(_options.MeasurementId) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            {
                _logger.LogWarning("[GoogleAnalyticsService][TrackEventAsync] GA configuration not properly set");
                return new GoogleAnalyticsEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "Google Analytics not configured"
                };
            }

            var payload = CreateMeasurementProtocolPayload(eventRequest);
            var jsonPayload = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            var url = BuildRequestUrl();
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _logger.LogDebug("[GoogleAnalyticsService][TrackEventAsync] Sending event: {EventName}, ClientId: {ClientId}, URL: {Url}",
                eventRequest.EventName, eventRequest.ClientId, url);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

            var response = await httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[GoogleAnalyticsService][TrackEventAsync] Event sent successfully: {EventName}",
                    eventRequest.EventName);

                return new GoogleAnalyticsEventResponseDto { Success = true };
            }
            else
            {
                _logger.LogError("[GoogleAnalyticsService][TrackEventAsync] Failed to send event: {EventName}, Status: {StatusCode}, Response: {Response}",
                    eventRequest.EventName, response.StatusCode, responseContent);

                return new GoogleAnalyticsEventResponseDto
                {
                    Success = false,
                    ErrorMessage = $"HTTP {response.StatusCode}: {responseContent}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAnalyticsService][TrackEventAsync] Exception occurred while sending event: {EventName}",
                eventRequest.EventName);

            return new GoogleAnalyticsEventResponseDto
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private GAMeasurementProtocolPayload CreateMeasurementProtocolPayload(GoogleAnalyticsEventRequestDto eventRequest)
    {
        var payload = new GAMeasurementProtocolPayload
        {
            ClientId = !string.IsNullOrWhiteSpace(eventRequest.ClientId) 
                ? eventRequest.ClientId 
                : "null",
            UserId = eventRequest.UserId,
            TimestampMicros = eventRequest.TimestampMicros ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
        };

        var gaEvent = new GAEvent
        {
            Name = eventRequest.EventName,
            Parameters = new Dictionary<string, object>(eventRequest.Parameters)
        };

        if (!string.IsNullOrWhiteSpace(eventRequest.SessionId))
        {
            gaEvent.Parameters["session_id"] = eventRequest.SessionId;
        }

        if (!string.IsNullOrWhiteSpace(eventRequest.UserId))
        {
            gaEvent.Parameters["user_id"] = eventRequest.UserId;
        }

        payload.Events.Add(gaEvent);
        return payload;
    }

    public async Task<GoogleAnalyticsEventResponseDto> TrackFirebaseEventAsync(GoogleAnalyticsEventRequestDto eventRequest)
    {
        try
        {
            if (!_firebaseOptions.EnableAnalytics)
            {
                _logger.LogDebug("[GoogleAnalyticsService][TrackFirebaseEventAsync] Firebase Analytics reporting is disabled");
                return new GoogleAnalyticsEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "Firebase Analytics reporting is disabled"
                };
            }
            
            if (string.IsNullOrWhiteSpace(_firebaseOptions.FirebaseAppId) || string.IsNullOrWhiteSpace(_firebaseOptions.ApiSecret))
            {
                _logger.LogWarning("[GoogleAnalyticsService][TrackFirebaseEventAsync] Firebase configuration not properly set");
                return new GoogleAnalyticsEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "Firebase Analytics not configured"
                };
            }

            var payload = CreateFirebaseMeasurementProtocolPayload(eventRequest);
            var jsonPayload = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            var url = BuildFirebaseRequestUrl();
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(_firebaseOptions.TimeoutSeconds);

            _logger.LogDebug("[GoogleAnalyticsService][TrackFirebaseEventAsync] Sending event to Firebase: {EventName}",
                eventRequest.EventName);

            var response = await httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[GoogleAnalyticsService][TrackFirebaseEventAsync] Firebase Analytics event sent successfully: {EventName}",
                    eventRequest.EventName);
                    
                return new GoogleAnalyticsEventResponseDto { Success = true };
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[GoogleAnalyticsService][TrackFirebaseEventAsync] Firebase Analytics API returned error: {StatusCode}",
                    response.StatusCode);
                    
                return new GoogleAnalyticsEventResponseDto
                {
                    Success = false,
                    ErrorMessage = $"Firebase API error: {response.StatusCode}"
                };
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "[GoogleAnalyticsService][TrackFirebaseEventAsync] Firebase Analytics API timeout");
            return new GoogleAnalyticsEventResponseDto
            {
                Success = false,
                ErrorMessage = "Request timeout"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[GoogleAnalyticsService][TrackFirebaseEventAsync] HTTP error sending event to Firebase");
            return new GoogleAnalyticsEventResponseDto
            {
                Success = false,
                ErrorMessage = "HTTP request failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAnalyticsService][TrackFirebaseEventAsync] Unexpected error sending event to Firebase");
            return new GoogleAnalyticsEventResponseDto
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private FirebaseMeasurementProtocolPayload CreateFirebaseMeasurementProtocolPayload(GoogleAnalyticsEventRequestDto eventRequest)
    {
        var payload = new FirebaseMeasurementProtocolPayload
        {
            AppInstanceId = !string.IsNullOrWhiteSpace(eventRequest.AppInstanceId) 
                ? eventRequest.AppInstanceId
                : "unknown"
        };

        var firebaseEvent = new FirebaseEvent
        {
            Name = eventRequest.EventName,
            Parameters = new Dictionary<string, object>(eventRequest.Parameters)
        };

        payload.Events.Add(firebaseEvent);
        return payload;
    }

    private string BuildRequestUrl()
    {
        var baseUrl = _options.ApiEndpoint;
        return $"{baseUrl}?measurement_id={_options.MeasurementId}&api_secret={_options.ApiSecret}";
    }

    private string BuildFirebaseRequestUrl()
    {
        var baseUrl = _firebaseOptions.ApiEndpoint;
        return $"{baseUrl}?firebase_app_id={_firebaseOptions.FirebaseAppId}&api_secret={_firebaseOptions.ApiSecret}";
    }

    public async Task<GoogleAnalyticsBatchEventResponseDto> TrackFirebaseBatchEventsAsync(GoogleAnalyticsBatchEventRequestDto batchRequest)
    {
        try
        {
            if (batchRequest.Events == null || !batchRequest.Events.Any())
            {
                return new GoogleAnalyticsBatchEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "No events provided"
                };
            }

            if (string.IsNullOrWhiteSpace(batchRequest.AppInstanceId))
            {
                return new GoogleAnalyticsBatchEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "AppInstanceId is required"
                };
            }

            if (!_firebaseOptions.EnableBatchAnalytics)
            {
                return new GoogleAnalyticsBatchEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "Firebase Analytics reporting is disabled"
                };
            }
            
            if (string.IsNullOrWhiteSpace(_firebaseOptions.FirebaseAppId) || string.IsNullOrWhiteSpace(_firebaseOptions.ApiSecret))
            {
                return new GoogleAnalyticsBatchEventResponseDto
                {
                    Success = false,
                    ErrorMessage = "Firebase Analytics not configured"
                };
            }

            var payload = new FirebaseMeasurementProtocolPayload
            {
                AppInstanceId = batchRequest.AppInstanceId
            };

            foreach (var eventDto in batchRequest.Events)
            {
                var firebaseEvent = new FirebaseEvent
                {
                    Name = eventDto.EventName,
                    Parameters = new Dictionary<string, object>(eventDto.Parameters)
                };
                payload.Events.Add(firebaseEvent);
            }

            var jsonPayload = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            var url = BuildFirebaseRequestUrl();
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(_firebaseOptions.TimeoutSeconds);

            var httpResponse = await httpClient.PostAsync(url, content);

            if (httpResponse.IsSuccessStatusCode)
            {
                return new GoogleAnalyticsBatchEventResponseDto { Success = true };
            }
            else
            {
                return new GoogleAnalyticsBatchEventResponseDto
                {
                    Success = false,
                    ErrorMessage = $"Firebase API error: {httpResponse.StatusCode}"
                };
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "[GoogleAnalyticsService][TrackFirebaseBatchEventsAsync] Firebase Analytics API timeout");
            return new GoogleAnalyticsBatchEventResponseDto
            {
                Success = false,
                ErrorMessage = "Request timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAnalyticsService][TrackFirebaseBatchEventsAsync] Unexpected error");
            return new GoogleAnalyticsBatchEventResponseDto
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
