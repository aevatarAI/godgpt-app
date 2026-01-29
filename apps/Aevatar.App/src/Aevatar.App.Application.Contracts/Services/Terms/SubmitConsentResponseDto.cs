using System;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// Response DTO for consent submission.
/// </summary>
public class SubmitConsentResponseDto
{
    /// <summary>
    /// Whether the consent was successfully recorded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Unique identifier for this consent record.
    /// </summary>
    public Guid ConsentId { get; set; }

    /// <summary>
    /// Timestamp when consent was recorded.
    /// </summary>
    public DateTime ConsentedAt { get; set; }

    /// <summary>
    /// Version that was consented to.
    /// </summary>
    public string Version { get; set; } = string.Empty;
}
