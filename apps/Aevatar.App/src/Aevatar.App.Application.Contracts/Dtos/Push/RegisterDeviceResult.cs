namespace Aevatar.Dtos.Push;

/// <summary>
/// Result of device registration.
/// </summary>
public class RegisterDeviceResult
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Whether this is a newly registered device (true) or an existing device update (false).
    /// </summary>
    public bool IsNewDevice { get; set; }
}
