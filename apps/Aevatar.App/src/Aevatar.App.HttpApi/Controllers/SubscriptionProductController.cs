using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// User-facing subscription products API.
/// </summary>
[RemoteService]
[Route("api/subscription")]
[Authorize]
public class SubscriptionProductController : AbpController
{
    private readonly ISubscriptionProductService _productService;

    public SubscriptionProductController(ISubscriptionProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// Get subscription products by platform.
    /// </summary>
    /// <param name="platform">Target platform (Stripe, Apple, Google)</param>
    /// <param name="currency">Preferred currency code (ISO 4217). Only used for Stripe platform.</param>
    /// <returns>List of subscription products</returns>
    [HttpGet("products")]
    public async Task<List<SubscriptionProductDto>> GetProductsAsync(
        [FromQuery, Required] PaymentPlatform platform,
        [FromQuery] string? currency = null)
    {
        return await _productService.GetListedProductsByPlatformAsync(platform, currency);
    }
}
