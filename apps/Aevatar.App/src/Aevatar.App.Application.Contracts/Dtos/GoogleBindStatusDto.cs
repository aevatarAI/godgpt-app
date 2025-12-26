namespace Aevatar.Dtos;

/// <summary>
/// Google account binding status
/// </summary>
public class GoogleBindStatusDto
{
    /// <summary>
    /// Whether Google account is bound
    /// </summary>
    public bool IsBound { get; set; }

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
    /// Whether calendar sync is enabled
    /// </summary>
    public bool CalendarSyncEnabled { get; set; }
}

