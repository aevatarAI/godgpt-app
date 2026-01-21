using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs;

/// <summary>
/// Configuration options for State Migration Job
/// </summary>
public class StateMigrationOptions
{
    public const string SectionName = "StateMigration";

    /// <summary>
    /// Enable/disable migration job
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Old system API base URL
    /// </summary>
    public string OldSystemApiBaseUrl { get; set; } = "http://localhost:8001";

    /// <summary>
    /// Token endpoint URL for fetching authentication token
    /// Required for API authentication
    /// </summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Username for token authentication
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for token authentication
    /// </summary>
    [JsonIgnore] // Don't serialize password to logs
    public string? Password { get; set; }

    /// <summary>
    /// Client ID for token authentication
    /// </summary>
    public string ClientId { get; set; } = "AevatarAuthServer";

    /// <summary>
    /// Scope for token authentication
    /// </summary>
    public string Scope { get; set; } = "Aevatar";

    /// <summary>
    /// Batch size for processing records
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Delay between batches (milliseconds)
    /// </summary>
    public int BatchDelayMs { get; set; } = 100;

    /// <summary>
    /// Collection type names to skip during migration
    /// </summary>
    public List<string> SkipCollections { get; set; } = new();

    /// <summary>
    /// Fixed collection names to migrate (bypasses GetCollections API)
    /// If not empty, uses this list instead of calling the old system API
    /// </summary>
    public List<string> FixedCollections { get; set; } = new();
}
