namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// Wrapper message for byte array data to be sent via MassTransit.
/// </summary>
public class ByteArrayMessage
{
    /// <summary>
    /// The Stream ID this message belongs to.
    /// </summary>
    public string StreamId { get; set; } = string.Empty;

    /// <summary>
    /// The raw byte data.
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
