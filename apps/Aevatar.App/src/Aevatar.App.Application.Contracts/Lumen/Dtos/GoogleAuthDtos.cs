using System;

namespace Aevatar.App.Lumen.Dtos;

/// <summary>
/// Input for Google OAuth verification
/// </summary>
public class GoogleAuthVerifyCodeInput
{
    public string Platform { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? CodeVerifier { get; set; }
}

/// <summary>
/// Result of Google OAuth verification
/// </summary>
public class GoogleAuthResultDto
{
    public bool Success { get; set; }
    public string? Email { get; set; }
    public string? Message { get; set; }
    public bool IsNewBinding { get; set; }
}

/// <summary>
/// Status of Google account binding
/// </summary>
public class GoogleBindStatusDto
{
    public bool IsBound { get; set; }
    public string? BoundEmail { get; set; }
    public DateTime? BoundAt { get; set; }
}

