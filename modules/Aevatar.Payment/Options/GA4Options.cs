namespace Aevatar.Payment.Options;

/// <summary>
/// Google Analytics 4 configuration options for payment reporting
/// </summary>
public class GA4Options
{
    public const string SectionName = "GoogleAnalytics";

    /// <summary>
    /// Enable/disable analytics reporting
    /// </summary>
    public bool EnableAnalytics { get; set; } = true;

    /// <summary>
    /// GA4 Measurement ID (G-XXXXXXXXXX)
    /// </summary>
    public string MeasurementId { get; set; } = string.Empty;

    /// <summary>
    /// GA4 Measurement Protocol API Secret
    /// </summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// GA4 Measurement Protocol endpoint
    /// </summary>
    public string ApiEndpoint { get; set; } = "https://www.google-analytics.com/mp/collect";

    /// <summary>
    /// HTTP request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 50;
}

