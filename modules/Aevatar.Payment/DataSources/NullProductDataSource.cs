using Aevatar.Payment.Abstractions;

namespace Aevatar.Payment.DataSources;

/// <summary>
/// Default null implementation of IProductDataSource.
/// Returns empty list, allowing providers to fall back to configuration.
/// </summary>
public class NullProductDataSource : IProductDataSource
{
    public Task<List<ProductDto>> GetProductsAsync(
        PaymentPlatform platform,
        string? currency = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(new List<ProductDto>());
    }

    public Task<ProductDto?> GetProductByPlatformProductIdAsync(
        PaymentPlatform platform,
        string productId,
        CancellationToken ct = default)
    {
        return Task.FromResult<ProductDto?>(null);
    }
}
