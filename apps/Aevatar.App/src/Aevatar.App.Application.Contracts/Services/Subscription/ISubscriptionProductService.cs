using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription.Dtos;

namespace Aevatar.App.Services.Subscription;

/// <summary>
/// Application service interface for subscription products.
/// </summary>
public interface ISubscriptionProductService
{
    #region User-facing queries

    /// <summary>
    /// Gets listed products by platform for user display.
    /// </summary>
    Task<List<SubscriptionProductDto>> GetListedProductsByPlatformAsync(
        PaymentPlatform platform,
        string? currency = null);

    #endregion

    #region Internal queries

    /// <summary>
    /// Gets listed products with price info only (no Features/Labels).
    /// Used by Payment providers for product listing.
    /// </summary>
    Task<List<SubscriptionProductWithPriceDto>> GetListedProductsWithPriceByPlatformAsync(
        PaymentPlatform platform,
        string? currency = null);

    Task<SubscriptionProductWithPriceDto?> GetProductByPlatformProductIdAsync(string platformPriceId, PaymentPlatform platform);

    Task<SubscriptionProductWithPriceDto?> GetProductByPlatformPriceIdAsync(string platformPriceId);

    #endregion

    #region Admin queries

    /// <summary>
    /// Gets all products for admin management.
    /// </summary>
    Task<List<SubscriptionProductAdminDto>> GetAllProductsAdminAsync(
        PaymentPlatform? platform = null,
        bool? isListed = null);

    /// <summary>
    /// Gets a product by ID for admin.
    /// </summary>
    Task<SubscriptionProductAdminDto?> GetProductAdminAsync(string productId);

    #endregion

    #region Admin mutations

    /// <summary>
    /// Creates a new subscription product.
    /// </summary>
    Task<SubscriptionProductAdminDto> CreateProductAsync(CreateProductDto dto);

    /// <summary>
    /// Updates an existing subscription product.
    /// </summary>
    Task<SubscriptionProductAdminDto> UpdateProductAsync(string productId, UpdateProductDto dto);

    /// <summary>
    /// Deletes a subscription product.
    /// </summary>
    Task DeleteProductAsync(string productId);

    /// <summary>
    /// Sets a product's listed status.
    /// </summary>
    Task<SubscriptionProductAdminDto> SetProductListedAsync(string productId, bool isListed);

    #endregion

    #region Price management (Product-scoped price operations)

    /// <summary>
    /// Sets a platform price for a product.
    /// </summary>
    Task<PlatformPriceDto> SetPriceAsync(string productId, SetPriceDto dto);

    /// <summary>
    /// Deletes a platform price from a product.
    /// </summary>
    Task DeletePriceAsync(string productId, PaymentPlatform platform, string currency);

    #endregion
}
