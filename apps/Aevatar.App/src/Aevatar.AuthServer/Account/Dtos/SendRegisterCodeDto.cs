using System.ComponentModel.DataAnnotations;

namespace Aevatar.AuthServer.Account.Dtos;

/// <summary>
/// DTO for sending registration verification code
/// </summary>
public class SendRegisterCodeDto
{
    /// <summary>
    /// Email address to send verification code to
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Application name (e.g., "GodGPT", "Lumen")
    /// </summary>
    [Required]
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Platform type: 0 = web, 1 = iOS, 2 = Android
    /// </summary>
    public int Platform { get; set; }

    /// <summary>
    /// reCAPTCHA token for web platform verification
    /// </summary>
    public string? RecaptchaToken { get; set; }
}

/// <summary>
/// Response for send register code operation
/// </summary>
public class SendRegisterCodeResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

