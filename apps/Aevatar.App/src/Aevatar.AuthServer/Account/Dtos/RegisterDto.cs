using System.ComponentModel.DataAnnotations;

namespace Aevatar.AuthServer.Account.Dtos;

/// <summary>
/// DTO for user registration
/// </summary>
public class RegisterDto
{
    /// <summary>
    /// Email address for the new account
    /// </summary>
    [Required]
    [EmailAddress]
    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// Username for the new account
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Password for the new account
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Verification code received via email
    /// </summary>
    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Application name (e.g., "GodGPT", "Lumen")
    /// </summary>
    [Required]
    public string AppName { get; set; } = string.Empty;
}

