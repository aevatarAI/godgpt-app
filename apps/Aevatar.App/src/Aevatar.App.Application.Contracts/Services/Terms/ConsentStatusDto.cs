using System;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// Response DTO for checking user's consent status.
/// </summary>
public class ConsentStatusDto
{
    /// <summary>
    /// The current active version of ToS.
    /// </summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user has consented to the current version.
    /// </summary>
    public bool HasConsented { get; set; }

    /// <summary>
    /// When the user gave consent (null if not consented).
    /// </summary>
    public DateTime? ConsentedAt { get; set; }

    /// <summary>
    /// URL to fetch the ToS content.
    /// </summary>
    public string TermsUrl { get; set; } = string.Empty;
}
