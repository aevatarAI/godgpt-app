namespace Aevatar.Payment.Abstractions;

/// <summary>
/// Data source for subscription products.
/// Default implementation returns empty (NullObject pattern).
/// App layer can override with GAgent-based implementation.
/// </summary>
public interface IProductDataSource
{
    /// <summary>
    /// Get products for a specific payment platform.
    /// </summary>
    /// <param name="platform">Target payment platform</param>
    /// <param name="currency">Preferred currency (optional)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of products, or empty list if not available</returns>
    Task<List<ProductDto>> GetProductsAsync(
        PaymentPlatform platform,
        string? currency = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single product by platform and product/price ID.
    /// Used e.g. for PeriodEnd calculation when product is not in config.
    /// </summary>
    /// <param name="platform">Target payment platform</param>
    /// <param name="productId">Platform product ID (Apple/Google) or price ID (Stripe)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Product if found, otherwise null</returns>
    Task<ProductDto?> GetProductByPlatformProductIdAsync(
        PaymentPlatform platform,
        string productId,
        CancellationToken ct = default);
}
