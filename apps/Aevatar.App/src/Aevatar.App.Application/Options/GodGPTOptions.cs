using System.Collections.Generic;

namespace Aevatar.Options;

public class GodGPTOptions
{
    public string Version { get; set; } = "1.20.0";
    
    /// <summary>
    /// Feature flags dictionary, key is feature name, value is whether enabled
    /// Example configuration:
    /// "VoiceFeature": true,
    /// "ImageUpload": true, 
    /// "ShareFeature": true,
    /// "ExperimentalFeature": false,
    /// "BetaMode": true
    /// </summary>
    public Dictionary<string, bool> Features { get; set; } = new();
}