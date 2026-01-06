using System.Collections.Generic;

namespace Aevatar.AuthServer.Account;

/// <summary>
/// Configuration options for account management.
/// Supports multiple applications with app-specific settings.
/// </summary>
public class AccountOptions
{
    /// <summary>
    /// Duration in minutes for registration code validity
    /// </summary>
    public int RegisterCodeDuration { get; set; } = 10;

    /// <summary>
    /// Minimum interval in minutes between email sends to same address
    /// </summary>
    public int MailSendingInterval { get; set; } = 1;

    /// <summary>
    /// Password reset token lifespan in minutes
    /// </summary>
    public long TokenLifespan { get; set; } = 1440;

    /// <summary>
    /// Default reset password URL (fallback if app-specific not found)
    /// </summary>
    public string DefaultResetPasswordUrl { get; set; } = string.Empty;

    /// <summary>
    /// App-specific configurations
    /// </summary>
    public Dictionary<string, AppAccountOptions> Apps { get; set; } = new();
}

/// <summary>
/// App-specific account configuration
/// </summary>
public class AppAccountOptions
{
    /// <summary>
    /// Password reset URL for this app
    /// </summary>
    public string ResetPasswordUrl { get; set; } = string.Empty;

    /// <summary>
    /// Password reset URL for China region
    /// </summary>
    public string CNResetPasswordUrl { get; set; } = string.Empty;

    /// <summary>
    /// Email sender display name for this app
    /// </summary>
    public string EmailFromName { get; set; } = string.Empty;
}

