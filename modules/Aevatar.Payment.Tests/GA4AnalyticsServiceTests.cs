using System.Net;
using Aevatar.Payment.Analytics;
using Aevatar.Payment.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aevatar.Payment.Tests;

/// <summary>
/// Unit tests for GA4AnalyticsService
/// </summary>
public class GA4AnalyticsServiceTests
{
    private readonly IOptionsMonitor<GA4Options> _mockOptions;
    private readonly GA4Options _options;

    public GA4AnalyticsServiceTests()
    {
        _options = new GA4Options
        {
            EnableAnalytics = true,
            MeasurementId = "G-TEST123",
            ApiSecret = "test_secret",
            ApiEndpoint = "https://www.google-analytics.com/mp/collect",
            TimeoutSeconds = 5,
            RetryCount = 3,
            RetryDelayMs = 10
        };

        _mockOptions = Substitute.For<IOptionsMonitor<GA4Options>>();
        _mockOptions.CurrentValue.Returns(_options);
    }

    [Fact]
    public async Task ReportPurchaseAsync_ShouldReturnTrue_WhenAnalyticsDisabled()
    {
        // Arrange
        _options.EnableAnalytics = false;
        var httpClient = new HttpClient();
        var service = new GA4AnalyticsService(
            NullLogger<GA4AnalyticsService>.Instance,
            _mockOptions,
            httpClient);

        // Act
        var result = await service.ReportPurchaseAsync(
            "Stripe", "txn_123", "user_456", 19.99m, "USD");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ReportPurchaseAsync_ShouldReturnFalse_WhenMissingConfig()
    {
        // Arrange
        _options.MeasurementId = "";
        var httpClient = new HttpClient();
        var service = new GA4AnalyticsService(
            NullLogger<GA4AnalyticsService>.Instance,
            _mockOptions,
            httpClient);

        // Act
        var result = await service.ReportPurchaseAsync(
            "Stripe", "txn_123", "user_456", 19.99m, "USD");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ReportPurchaseAsync_ShouldReturnTrue_OnSuccessfulRequest()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent);
        var httpClient = new HttpClient(handler);
        var service = new GA4AnalyticsService(
            NullLogger<GA4AnalyticsService>.Instance,
            _mockOptions,
            httpClient);

        // Act
        var result = await service.ReportPurchaseAsync(
            "Stripe", "txn_123", "user_456", 19.99m, "USD");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ReportPurchaseAsync_ShouldRetry_On5xxError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.ServiceUnavailable, // First 2 calls
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.NoContent);         // Third call succeeds
        var httpClient = new HttpClient(handler);
        var service = new GA4AnalyticsService(
            NullLogger<GA4AnalyticsService>.Instance,
            _mockOptions,
            httpClient);

        // Act
        var result = await service.ReportPurchaseAsync(
            "Stripe", "txn_123", "user_456", 19.99m, "USD");

        // Assert
        Assert.True(result);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ReportPurchaseAsync_ShouldNotRetry_On4xxError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.BadRequest);
        var httpClient = new HttpClient(handler);
        var service = new GA4AnalyticsService(
            NullLogger<GA4AnalyticsService>.Instance,
            _mockOptions,
            httpClient);

        // Act
        var result = await service.ReportPurchaseAsync(
            "Stripe", "txn_123", "user_456", 19.99m, "USD");

        // Assert
        Assert.False(result);
        Assert.Equal(1, handler.CallCount); // No retry on 4xx
    }

    [Fact]
    public async Task ReportRefundAsync_ShouldReturnTrue_OnSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent);
        var httpClient = new HttpClient(handler);
        var service = new GA4AnalyticsService(
            NullLogger<GA4AnalyticsService>.Instance,
            _mockOptions,
            httpClient);

        // Act
        var result = await service.ReportRefundAsync(
            "AppStore", "txn_original", "user_456", 9.99m, "USD", "Customer request");

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("Stripe")]
    [InlineData("AppStore")]
    [InlineData("GooglePlay")]
    public async Task ReportPurchaseAsync_ShouldFormatTransactionId_WithPlatform(string platform)
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent);
        var httpClient = new HttpClient(handler);
        var service = new GA4AnalyticsService(
            NullLogger<GA4AnalyticsService>.Instance,
            _mockOptions,
            httpClient);

        // Act
        await service.ReportPurchaseAsync(platform, "txn_123", "user_456", 19.99m, "USD");

        // Assert
        Assert.NotNull(handler.LastRequestContent);
        Assert.Contains($"user_456^{platform}^txn_123", handler.LastRequestContent);
    }
}

/// <summary>
/// Mock HTTP message handler for testing
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpStatusCode> _statusCodes;
    public int CallCount { get; private set; }
    public string? LastRequestContent { get; private set; }

    public MockHttpMessageHandler(params HttpStatusCode[] statusCodes)
    {
        _statusCodes = new Queue<HttpStatusCode>(statusCodes);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        
        // Read content before it's disposed
        if (request.Content != null)
        {
            LastRequestContent = await request.Content.ReadAsStringAsync(cancellationToken);
        }
        
        var statusCode = _statusCodes.Count > 0 
            ? _statusCodes.Dequeue() 
            : HttpStatusCode.OK;
            
        return new HttpResponseMessage(statusCode);
    }
}
