using System;

namespace Aevatar.App.Application.Common;

/// <summary>
/// Utility class for compressing and decompressing multiple GUIDs into a URL-safe string.
/// Used primarily for generating shareable links.
/// </summary>
public static class GuidCompressor
{
    /// <summary>
    /// Compresses three GUIDs into a URL-safe Base64 string.
    /// </summary>
    /// <param name="guid1">First GUID (typically userId)</param>
    /// <param name="guid2">Second GUID (typically sessionId)</param>
    /// <param name="guid3">Third GUID (typically shareId)</param>
    /// <returns>A URL-safe compressed string representation</returns>
    public static string CompressGuids(Guid guid1, Guid guid2, Guid guid3)
    {
        var combinedBytes = CombineBytes(guid1.ToByteArray(), guid2.ToByteArray());
        combinedBytes = CombineBytes(combinedBytes, guid3.ToByteArray());

        var base64 = Convert.ToBase64String(combinedBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "");
        return base64;
    }

    /// <summary>
    /// Decompresses a URL-safe Base64 string back into three GUIDs.
    /// </summary>
    /// <param name="compressedString">The compressed string to decompress</param>
    /// <returns>A tuple containing the three original GUIDs</returns>
    /// <exception cref="FormatException">Thrown when the compressed string is invalid</exception>
    public static (Guid, Guid, Guid) DecompressGuids(string compressedString)
    {
        var restoredBase64 = compressedString
            .Replace('-', '+')
            .Replace('_', '/')
            .PadRight(compressedString.Length + (4 - compressedString.Length % 4) % 4, '=');

        var bytes = Convert.FromBase64String(restoredBase64);

        var guid1Bytes = new byte[16];
        var guid2Bytes = new byte[16];
        var guid3Bytes = new byte[16];
        Array.Copy(bytes, 0, guid1Bytes, 0, 16);
        Array.Copy(bytes, 16, guid2Bytes, 0, 16);
        Array.Copy(bytes, 32, guid3Bytes, 0, 16);

        return (new Guid(guid1Bytes), new Guid(guid2Bytes), new Guid(guid3Bytes));
    }

    private static byte[] CombineBytes(byte[] bytes1, byte[] bytes2)
    {
        var combined = new byte[bytes1.Length + bytes2.Length];
        Buffer.BlockCopy(bytes1, 0, combined, 0, bytes1.Length);
        Buffer.BlockCopy(bytes2, 0, combined, bytes1.Length, bytes2.Length);
        return combined;
    }
}
