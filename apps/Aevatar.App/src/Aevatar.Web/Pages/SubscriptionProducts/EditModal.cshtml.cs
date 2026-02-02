using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Localization;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace Aevatar.Web.Pages.SubscriptionProducts;

[Authorize(Policy = SubscriptionProductPermissions.Products.Update)]
public class EditModalModel : AevatarPageModel
{
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; }

    [BindProperty]
    public EditProductInput Product { get; set; } = new();

    public List<SelectListItem> AvailableFeatures { get; private set; } = new();
    public List<SelectListItem> AvailableLabels { get; private set; } = new();
    public List<PriceDisplayItem> Prices { get; private set; } = new();
    public bool IsStripePlatform { get; private set; }

    public List<SelectListItem> PlanTypeOptions { get; } = Enum.GetValues<PlanType>()
        .Select(p => new SelectListItem(p.ToString(), ((int)p).ToString()))
        .ToList();

    private readonly ISubscriptionProductService _productService;
    private readonly ISubscriptionFeatureService _featureService;
    private readonly ISubscriptionLabelService _labelService;
    private readonly IStringLocalizer<AevatarResource> _localizer;

    public EditModalModel(
        ISubscriptionProductService productService,
        ISubscriptionFeatureService featureService,
        ISubscriptionLabelService labelService,
        IStringLocalizer<AevatarResource> localizer)
    {
        _productService = productService;
        _featureService = featureService;
        _labelService = labelService;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var product = await _productService.GetProductAdminAsync(Id);
        if (product == null) return NotFound();

        Product = new EditProductInput
        {
            NameKey = product.NameKey,
            PlanType = product.PlanType,
            DescriptionKey = product.DescriptionKey,
            HighlightKey = product.HighlightKey,
            IsUltimate = product.IsUltimate,
            Platform = product.Platform,
            PlatformProductId = product.PlatformProductId,
            FeatureIds = product.FeatureIds,
            LabelId = product.LabelId,
            DisplayOrder = product.DisplayOrder
        };

        IsStripePlatform = product.Platform == PaymentPlatform.Stripe;

        // Load prices for display (Stripe only)
        if (IsStripePlatform && product.Prices.Any())
        {
            Prices = product.Prices.Select(p => new PriceDisplayItem
            {
                Price = p.Price,
                Currency = p.Currency,
                FormattedPrice = FormatPrice(p.Price, p.Currency)
            }).ToList();
        }

        // Load available features
        var features = await _featureService.GetAllFeaturesAdminAsync();
        AvailableFeatures = features.Select(f => new SelectListItem(
            FormatFeatureDisplayText(f),
            f.Id.ToString(),
            Product.FeatureIds?.Contains(f.Id) == true
        )).ToList();

        // Load available labels
        var labels = await _labelService.GetAllLabelsAsync();
        AvailableLabels = new List<SelectListItem> { new SelectListItem("- None -", "") };
        AvailableLabels.AddRange(labels.Select(l => new SelectListItem(
            l.Name,
            l.Id.ToString(),
            Product.LabelId == l.Id
        )));

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var updateDto = new UpdateProductDto
        {
            NameKey = Product.NameKey!,
            PlanType = Product.PlanType,
            DescriptionKey = Product.DescriptionKey!,
            IsUltimate = Product.IsUltimate,
            HighlightKey = Product.HighlightKey ?? string.Empty,
            LabelId = Product.LabelId ?? string.Empty,
            DisplayOrder = Product.DisplayOrder
        };

        if (Product.FeatureIds != null && Product.FeatureIds.Count > 0)
        {
            updateDto.FeatureIds.AddRange(Product.FeatureIds);
        }

        await _productService.UpdateProductAsync(Id, updateDto);
        return NoContent();
    }

    private string FormatPrice(double price, string currency)
    {
        return $"{currency} {price:N2}";
    }

    private static string FormatFeatureDisplayText(SubscriptionFeatureAdminDto f)
    {
        var usageText = f.Usage != null ? $" [{f.Usage}]" : "";
        return $"{f.Name} ({f.TypeName}){usageText}";
    }

    public string GetPlatformDisplayName(PaymentPlatform platform) => platform switch
    {
        PaymentPlatform.Stripe => "Stripe",
        PaymentPlatform.AppStore => "Apple",
        PaymentPlatform.GooglePlay => "Google",
        _ => platform.ToString()
    };

    public class EditProductInput
    {
        [Required]
        [MaxLength(256)]
        [Display(Name = "NameKey")]
        public string? NameKey { get; set; }

        [Required]
        [Display(Name = "PlanType")]
        public PlanType PlanType { get; set; }

        [Required]
        [MaxLength(256)]
        [Display(Name = "DescriptionKey")]
        public string? DescriptionKey { get; set; }

        [MaxLength(256)]
        [Display(Name = "HighlightKey")]
        public string? HighlightKey { get; set; }

        [Display(Name = "IsUltimate")]
        public bool IsUltimate { get; set; }

        // Read-only, displayed but not editable
        public PaymentPlatform Platform { get; set; }
        public string? PlatformProductId { get; set; }

        [Display(Name = "Features")]
        public List<string>? FeatureIds { get; set; }

        [Display(Name = "Label")]
        public string? LabelId { get; set; }

        [Display(Name = "DisplayOrder")]
        public int DisplayOrder { get; set; }
    }

    public class PriceDisplayItem
    {
        public double Price { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string FormattedPrice { get; set; } = string.Empty;
    }
}
