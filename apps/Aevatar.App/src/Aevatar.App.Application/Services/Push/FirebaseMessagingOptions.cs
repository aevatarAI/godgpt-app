namespace Aevatar.App.Application.Services.Push;

/// <summary>
/// Configuration options for Firebase Cloud Messaging.
/// </summary>
public class FirebaseMessagingOptions
{
    public const string SectionName = "FirebaseMessaging";
    
    /// <summary>
    /// Firebase project ID.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;
    
    /// <summary>
    /// Service account JSON content for OAuth2 authentication.
    /// </summary>
    public string ServiceAccountJson { get; set; } = string.Empty;
    
    /// <summary>
    /// Path to service account JSON file (alternative to ServiceAccountJson).
    /// </summary>
    public string? ServiceAccountJsonPath { get; set; }
    
    /// <summary>
    /// FCM API endpoint template.
    /// </summary>
    public string ApiEndpoint { get; set; } = "https://fcm.googleapis.com/v1/projects/{0}/messages:send";
    
    /// <summary>
    /// HTTP request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum number of concurrent push requests.
    /// </summary>
    public int MaxConcurrency { get; set; } = 10;
    
    /// <summary>
    /// Whether Firebase messaging is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
