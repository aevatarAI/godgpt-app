using System;
using System.ComponentModel.DataAnnotations;

namespace Aevatar.AuthServer.Account.Dtos;

/// <summary>
/// DTO for sending password reset code
/// </summary>
public class SendPasswordResetCodeDto
{
    /// <summary>
    /// Email address to send reset link to
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Application name
    /// </summary>
    [Required]
    public string AppName { get; set; } = string.Empty;
}

/// <summary>
/// DTO for verifying password reset token
/// </summary>
public class VerifyPasswordResetTokenDto
{
    /// <summary>
    /// User ID from the reset link
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Reset token from the email link
    /// </summary>
    [Required]
    public string ResetToken { get; set; } = string.Empty;
}

/// <summary>
/// DTO for resetting password
/// </summary>
public class ResetPasswordDto
{
    /// <summary>
    /// User ID from the reset link
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Reset token from the email link
    /// </summary>
    [Required]
    public string ResetToken { get; set; } = string.Empty;

    /// <summary>
    /// New password
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}

