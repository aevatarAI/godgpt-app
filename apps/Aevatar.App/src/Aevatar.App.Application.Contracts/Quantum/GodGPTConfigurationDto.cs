using System;
using System.Collections.Generic;

namespace Aevatar.Quantum;

public class GodGPTConfigurationDto
{
    public string SystemPrompt { get; set; } = string.Empty;
}

/// <summary>
/// GodGPT configuration response DTO for returning configuration information to frontend
/// </summary>
public class GodGPTConfigResponseDto
{
    /// <summary>
    /// Version information
    /// </summary>
    public string Version { get; set; } = string.Empty;
    
    /// <summary>
    /// Feature flags configuration
    /// </summary>
    public Dictionary<string, bool> Features { get; set; } = new();
}