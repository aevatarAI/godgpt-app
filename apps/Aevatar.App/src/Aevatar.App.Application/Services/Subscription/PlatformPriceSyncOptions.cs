namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Configuration options for platform price synchronization.
/// </summary>
public class PlatformPriceSyncOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public static string SectionName = "PlatformPriceSync";
    
    /// <summary>
    /// Whether the platform price sync is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Interval in hours between sync operations.
    /// Default: 24 hours
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 60;
}
