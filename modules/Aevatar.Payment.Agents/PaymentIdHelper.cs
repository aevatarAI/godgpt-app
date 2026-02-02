using System.Security.Cryptography;
using System.Text;

namespace Aevatar.Payment.Agents;

/// <summary>
/// Helper for generating stable Agent IDs from payment identifiers.
/// Ensures consistent ID generation across PaymentService and other consumers.
/// </summary>
public static class PaymentIdHelper
{
    /// <summary>
    /// Converts a paymentId string to a stable GUID for Agent ID.
    /// Uses MD5 hash to generate deterministic GUID from any string.
    /// </summary>
    /// <param name="paymentId">The payment identifier (e.g., "payment_stripe_xxx")</param>
    /// <returns>A stable GUID derived from the paymentId</returns>
    public static Guid ToAgentId(string paymentId)
    {
        if (string.IsNullOrEmpty(paymentId))
            throw new ArgumentException("paymentId cannot be null or empty", nameof(paymentId));
            
        var guidBytes = new byte[16];
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(paymentId));
        Array.Copy(hashBytes, guidBytes, 16);
        return new Guid(guidBytes);
    }
    
    /// <summary>
    /// Converts a paymentId string to a stable Agent ID string.
    /// </summary>
    /// <param name="paymentId">The payment identifier (e.g., "payment_stripe_xxx")</param>
    /// <returns>A stable GUID string derived from the paymentId</returns>
    public static string ToAgentIdString(string paymentId)
    {
        return ToAgentId(paymentId).ToString();
    }
}
