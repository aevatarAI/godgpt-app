using System;

namespace Aevatar.Quantum;

public class CreateSessionRequestDto
{
    public string? Guider { get; set; }
    public DateTime? UserLocalTime { get; set; } = null;
} 