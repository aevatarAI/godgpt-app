using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// Admin API for managing subscription products.
/// </summary>
[RemoteService]
[Route("api/admin/subscription/products")]
[Authorize]
public class SubscriptionProductAdminController : AbpController
{
    private readonly ISubscriptionProductService _productService;
    private readonly IPlatformPriceSyncService _platformSyncService;

    public SubscriptionProductAdminController(
        ISubscriptionProductService productService,
        IPlatformPriceSyncService platformSyncService)
    {
        _productService = productService;
        _platformSyncService = platformSyncService;
    }

    #region Product CRUD

    /// <summary>
    /// List all subscription products.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = SubscriptionProductPermissions.Products.Default)]
    public async Task<List<SubscriptionProductAdminDto>> GetListAsync(
        [FromQuery] PaymentPlatform? platform = null,
        [FromQuery] bool? isListed = null)
    {
        return await _productService.GetAllProductsAdminAsync(platform, isListed);
    }

    /// <summary>
    /// Get subscription product by ID.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = SubscriptionProductPermissions.Products.Default)]
    public async Task<ActionResult<SubscriptionProductAdminDto>> GetAsync(string id)
    {
        var product = await _productService.GetProductAdminAsync(id);
        if (product == null) return NotFound();
        return product;
    }

    /// <summary>
    /// Create a subscription product.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = SubscriptionProductPermissions.Products.Create)]
    public async Task<SubscriptionProductAdminDto> CreateAsync([FromBody] CreateProductDto input)
    {
        return await _productService.CreateProductAsync(input);
    }

    /// <summary>
    /// Update a subscription product.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = SubscriptionProductPermissions.Products.Update)]
    public async Task<SubscriptionProductAdminDto> UpdateAsync(string id, [FromBody] UpdateProductDto input)
    {
        return await _productService.UpdateProductAsync(id, input);
    }

    /// <summary>
    /// Delete a subscription product.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = SubscriptionProductPermissions.Products.Delete)]
    public async Task<IActionResult> DeleteAsync(string id)
    {
        await _productService.DeleteProductAsync(id);
        return NoContent();
    }

    /// <summary>
    /// List (上架) a subscription product.
    /// </summary>
    [HttpPost("{id}/list")]
    [Authorize(Policy = SubscriptionProductPermissions.Products.SetListed)]
    public async Task<SubscriptionProductAdminDto> ListAsync(string id)
    {
        return await _productService.SetProductListedAsync(id, true);
    }

    /// <summary>
    /// Unlist (下架) a subscription product.
    /// </summary>
    [HttpPost("{id}/unlist")]
    [Authorize(Policy = SubscriptionProductPermissions.Products.SetListed)]
    public async Task<SubscriptionProductAdminDto> UnlistAsync(string id)
    {
        return await _productService.SetProductListedAsync(id, false);
    }

    #endregion

    #region Product Price Management

    /// <summary>
    /// Set price for a product.
    /// </summary>
    [HttpPost("{id}/prices")]
    [Authorize(Policy = SubscriptionProductPermissions.Prices.Set)]
    public async Task<PlatformPriceDto> SetPriceAsync(string id, [FromBody] SetPriceDto input)
    {
        return await _productService.SetPriceAsync(id, input);
    }

    /// <summary>
    /// Delete price from a product.
    /// </summary>
    [HttpDelete("{id}/prices")]
    [Authorize(Policy = SubscriptionProductPermissions.Prices.Delete)]
    public async Task<IActionResult> DeletePriceAsync(
        string id,
        [FromQuery] PaymentPlatform platform,
        [FromQuery] string currency)
    {
        await _productService.DeletePriceAsync(id, platform, currency);
        return NoContent();
    }

    #endregion

    #region Price Sync

    /// <summary>
    /// Sync prices for a specific product.
    /// </summary>
    [HttpPost("{id}/sync-prices")]
    [Authorize(Policy = SubscriptionProductPermissions.Prices.SyncPrice)]
    public async Task<IActionResult> SyncProductPricesAsync(string id)
    {
        await _platformSyncService.SyncProductPricesAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Get last Stripe sync time.
    /// </summary>
    [HttpGet("sync-stripe/status")]
    [Authorize(Policy = SubscriptionProductPermissions.Prices.SyncPrice)]
    public async Task<ActionResult<DateTime>> GetLastSyncTimeAsync()
    {
        return await _platformSyncService.GetLastSyncTimeAsync();
    }

    #endregion
}
