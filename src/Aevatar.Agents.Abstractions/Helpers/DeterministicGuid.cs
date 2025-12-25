using System.Security.Cryptography;
using System.Text;

namespace Aevatar.Agents.Abstractions.Helpers;

// ============================================================
//  Deterministic Guid
//  确定性 Guid（用于“同一个 session => 同一组 ActorId”）
//
//  WHY:
//  - 前端要做到“无状态刷新”：刷新后服务端必须能稳定定位同一批 Agent
//  - Local/Orleans/ProtoActor 运行时都以 Guid 作为 Actor key
//  - 我们用 hash(name) -> Guid 的方式，避免在外层维护额外映射表
// ============================================================
public static class DeterministicGuid
{
    /// <summary>
    /// Create a deterministic Guid from an input string.
    /// </summary>
    public static Guid FromString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("input is required", nameof(input));

        // NOTE:
        // - SHA256 is stable and widely available.
        // - We only take the first 16 bytes as Guid payload.
        // - Keep the algorithm in ONE place so all modules use identical IDs.
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input.Trim()));

        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);

        // Set version/variant bits (best-effort; not strictly required but helps debugging).
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5-ish
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(guidBytes);
    }
}

