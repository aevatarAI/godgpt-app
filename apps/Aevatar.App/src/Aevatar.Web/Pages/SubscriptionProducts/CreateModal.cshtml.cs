using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aevatar.Web.Pages.SubscriptionProducts;

[Authorize(Policy = SubscriptionProductPermissions.Products.Create)]
public class CreateModalModel : AevatarPageModel
{
    [BindProperty]
    public CreateProductInput Product { get; set; } = new();

    public List<SelectListItem> PlatformOptions { get; } = Enum.GetValues<PaymentPlatform>()
        .Select(p => new SelectListItem(GetPlatformDisplayName(p), ((int)p).ToString()))
        .ToList();

    private static string GetPlatformDisplayName(PaymentPlatform platform) => platform switch
    {
        PaymentPlatform.Stripe => "Stripe",
        PaymentPlatform.AppStore => "Apple",
        PaymentPlatform.GooglePlay => "Google",
        _ => platform.ToString()
    };

    public List<SelectListItem> PlanTypeOptions { get; } = Enum.GetValues<PlanType>()
        .Select(p => new SelectListItem(p.ToString(), ((int)p).ToString()))
        .ToList();

    public List<SelectListItem> AvailableFeatures { get; private set; } = new();
    public List<SelectListItem> AvailableLabels { get; private set; } = new();

    private readonly ISubscriptionProductService _productService;
    private readonly ISubscriptionFeatureService _featureService;
    private readonly ISubscriptionLabelService _labelService;

    public CreateModalModel(
        ISubscriptionProductService productService,
        ISubscriptionFeatureService featureService,
        ISubscriptionLabelService labelService)
    {
        _productService = productService;
        _featureService = featureService;
        _labelService = labelService;
    }

    public async Task OnGetAsync()
    {
        // Load available features
        var features = await _featureService.GetAllFeaturesAdminAsync();
        AvailableFeatures = features.Select(f => new SelectListItem(
            FormatFeatureDisplayText(f),
            f.Id.ToString()
        )).ToList();

        // Load available labels
        var labels = await _labelService.GetAllLabelsAsync();
        AvailableLabels = new List<SelectListItem> { new SelectListItem("- None -", "") };
        AvailableLabels.AddRange(labels.Select(l => new SelectListItem(l.Name, l.Id.ToString())));
    }

    private static string FormatFeatureDisplayText(SubscriptionFeatureAdminDto f)
    {
        var usageText = f.Usage != null ? $" [{f.Usage}]" : "";
        return $"{f.Name} ({f.TypeName}){usageText}";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var createDto = new CreateProductDto
        {
            NameKey = Product.NameKey!,
            PlanType = Product.PlanType,
            DescriptionKey = Product.DescriptionKey!,
            IsUltimate = Product.IsUltimate,
            Platform = Product.Platform,
            PlatformProductId = Product.PlatformProductId!,
            DisplayOrder = Product.DisplayOrder
        };

        // Set optional fields only when they have values
        // (Protobuf setters throw ArgumentNullException for null values)
        if (!string.IsNullOrEmpty(Product.HighlightKey))
        {
            createDto.HighlightKey = Product.HighlightKey;
        }
        
        if (!string.IsNullOrEmpty(Product.LabelId))
        {
            createDto.LabelId = Product.LabelId;
        }

        if (Product.FeatureIds != null && Product.FeatureIds.Count > 0)
        {
            createDto.FeatureIds.AddRange(Product.FeatureIds);
        }

        await _productService.CreateProductAsync(createDto);
        return NoContent();
    }

    public class CreateProductInput
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

        [Required]
        [Display(Name = "Platform")]
        public PaymentPlatform Platform { get; set; }

        [Required]
        [MaxLength(256)]
        [Display(Name = "PlatformProductId")]
        public string? PlatformProductId { get; set; }

        [Display(Name = "Features")]
        public List<string>? FeatureIds { get; set; }

        [Display(Name = "Label")]
        public string? LabelId { get; set; }

        [Display(Name = "DisplayOrder")]
        public int DisplayOrder { get; set; }
    }
}
