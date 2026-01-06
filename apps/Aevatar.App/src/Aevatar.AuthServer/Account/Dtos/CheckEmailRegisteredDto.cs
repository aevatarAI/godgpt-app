using System.ComponentModel.DataAnnotations;

namespace Aevatar.AuthServer.Account.Dtos;

/// <summary>
/// DTO for checking if email is registered
/// </summary>
public class CheckEmailRegisteredDto
{
    /// <summary>
    /// Email address to check
    /// </summary>
    [Required]
    [EmailAddress]
    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// Application name (optional, for future multi-tenant support)
    /// </summary>
    public string? AppName { get; set; }
}

