using System.ComponentModel.DataAnnotations;

namespace Aevatar.AuthServer.Account.Dtos;

/// <summary>
/// DTO for verifying registration code
/// </summary>
public class VerifyRegisterCodeDto
{
    /// <summary>
    /// Email address that received the code
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 6-digit verification code
    /// </summary>
    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Application name
    /// </summary>
    public string AppName { get; set; } = string.Empty;
}

