namespace Aevatar.Dtos;

/// <summary>
/// Google OAuth2 authentication result
/// </summary>
public class GoogleAuthResultDto
{
    /// <summary>
    /// Whether the authentication was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if authentication failed
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Google user ID
    /// </summary>
    public string GoogleUserId { get; set; } = string.Empty;

    /// <summary>
    /// Google email
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Google display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the Google account is bound
    /// </summary>
    public bool BindStatus { get; set; }

    /// <summary>
    /// Redirect URI after authentication
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;
}

