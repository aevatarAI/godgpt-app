using System;

namespace Aevatar.App;

public class AuthTestDto
{
    public string Message { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsAuthenticated { get; set; }
}

