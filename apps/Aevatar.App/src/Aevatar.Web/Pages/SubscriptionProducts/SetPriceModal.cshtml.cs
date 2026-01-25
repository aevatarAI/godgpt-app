// ABOUTME: This file handles the Set Price modal for GooglePlay subscription products.
// ABOUTME: Allows admins to manually set prices for GooglePlay platform products.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aevatar.Web.Pages.SubscriptionProducts;

[Authorize(Policy = SubscriptionProductPermissions.Prices.Set)]
public class SetPriceModalModel : AevatarPageModel
{
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; }

    [BindProperty]
    public SetPriceInput Input { get; set; } = new();

    public string ProductName { get; private set; } = string.Empty;
    public string PlatformProductId { get; private set; } = string.Empty;

    public List<SelectListItem> CurrencyOptions { get; } = new()
    {
        new SelectListItem("USD - US Dollar", "USD"),
        new SelectListItem("EUR - Euro", "EUR"),
        new SelectListItem("GBP - British Pound", "GBP"),
        new SelectListItem("CNY - Chinese Yuan", "CNY"),
        new SelectListItem("JPY - Japanese Yen", "JPY"),
        new SelectListItem("KRW - Korean Won", "KRW"),
        new SelectListItem("HKD - Hong Kong Dollar", "HKD"),
        new SelectListItem("TWD - Taiwan Dollar", "TWD"),
        new SelectListItem("SGD - Singapore Dollar", "SGD"),
        new SelectListItem("AUD - Australian Dollar", "AUD"),
        new SelectListItem("CAD - Canadian Dollar", "CAD"),
        new SelectListItem("INR - Indian Rupee", "INR"),
        new SelectListItem("BRL - Brazilian Real", "BRL"),
        new SelectListItem("MXN - Mexican Peso", "MXN")
    };

    private readonly ISubscriptionProductService _productService;

    public SetPriceModalModel(ISubscriptionProductService productService)
    {
        _productService = productService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var product = await _productService.GetProductAdminAsync(Id);
        if (product == null) return NotFound();

        // Only allow setting price for GooglePlay platform
        if (product.Platform != PaymentPlatform.GooglePlay)
        {
            return BadRequest("Set price is only available for GooglePlay platform.");
        }

        ProductName = product.Name;
        PlatformProductId = product.PlatformProductId;

        // Pre-fill with existing price if available
        var existingPrice = product.Prices.Find(p => p.Currency == "USD");
        if (existingPrice != null)
        {
            Input.Currency = existingPrice.Currency;
            Input.Price = existingPrice.Price;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Re-fetch product to get PlatformProductId for the price ID
        var product = await _productService.GetProductAdminAsync(Id);
        if (product == null) return NotFound();

        var setPriceDto = new SetPriceDto
        {
            Platform = PaymentPlatform.GooglePlay,
            PlatformPriceId = string.Empty,
            Price = Input.Price,
            Currency = Input.Currency
        };
        await _productService.SetPriceAsync(Id, setPriceDto);

        return NoContent();
    }

    public class SetPriceInput
    {
        [Required]
        [Display(Name = "Currency")]
        public string Currency { get; set; } = "USD";

        [Required]
        [Range(0.01, 99999.99, ErrorMessage = "Price must be between 0.01 and 99999.99")]
        [Display(Name = "Price")]
        public double Price { get; set; }
    }
}
