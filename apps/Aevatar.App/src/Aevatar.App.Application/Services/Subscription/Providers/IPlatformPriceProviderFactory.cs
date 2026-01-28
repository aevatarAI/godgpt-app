using PaymentPlatform = Aevatar.Agents.GodGPT.Protos.InviteCode.PaymentPlatform;

namespace Aevatar.App.Services.Subscription.Providers;

/// <summary>
/// Factory for getting platform-specific price providers.
/// </summary>
public interface IPlatformPriceProviderFactory
{
    /// <summary>
    /// Get the provider for a specific platform.
    /// </summary>
    /// <param name="platform">The payment platform</param>
    /// <returns>The provider for the platform</returns>
    /// <exception cref="System.NotSupportedException">Thrown if no provider registered for platform</exception>
    IPlatformPriceProvider GetProvider(PaymentPlatform platform);
    
    /// <summary>
    /// Check if a provider exists for the platform.
    /// </summary>
    /// <param name="platform">The payment platform</param>
    /// <returns>True if provider exists</returns>
    bool HasProvider(PaymentPlatform platform);
}
