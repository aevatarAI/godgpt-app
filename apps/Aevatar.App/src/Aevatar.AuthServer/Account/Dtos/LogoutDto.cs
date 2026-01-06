namespace Aevatar.AuthServer.Account.Dtos;

/// <summary>
/// Result of logout operation
/// </summary>
public class LogoutResultDto
{
    /// <summary>
    /// Whether the logout was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Message describing the result
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

