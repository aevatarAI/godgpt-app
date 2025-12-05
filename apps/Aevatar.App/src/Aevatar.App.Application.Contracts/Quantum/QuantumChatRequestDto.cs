using System;
using System.Collections.Generic;

namespace Aevatar.Quantum;

public class QuantumChatRequestDto
{
    public Guid SessionId { get; set; }
    public string Content { get; set; }
    public string? Region { get; set; }
    public List<string> Images { get; set; } = new List<string>();
    public DateTime? UserLocalTime { get; set; } = null;
    public string? UserTimeZoneId { get; set; } = string.Empty;
}