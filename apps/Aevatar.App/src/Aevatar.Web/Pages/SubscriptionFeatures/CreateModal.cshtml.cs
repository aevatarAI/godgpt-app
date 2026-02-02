using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aevatar.Web.Pages.SubscriptionFeatures;

[Authorize(Policy = SubscriptionProductPermissions.Features.Create)]
public class CreateModalModel : AevatarPageModel
{
    [BindProperty]
    public CreateFeatureInput Feature { get; set; } = new();

    public List<SelectListItem> TypeOptions { get; } = new()
    {
        new SelectListItem("None", SubscriptionFeatureType.None.ToString()),
        new SelectListItem("Core Feature", SubscriptionFeatureType.Core.ToString()),
        new SelectListItem("Advanced Feature", SubscriptionFeatureType.Advanced.ToString())
    };

    public List<SelectListItem> UsageOptions { get; } = Enum.GetValues<SubscriptionFeatureUsage>()
        .Select(u => new SelectListItem(u.ToString(), u.ToString()))
        .ToList();

    private readonly ISubscriptionFeatureService _featureService;

    public CreateModalModel(ISubscriptionFeatureService featureService)
    {
        _featureService = featureService;
    }

    public void OnGet()
    {
        Feature = new CreateFeatureInput
        {
            Type = SubscriptionFeatureType.Core.ToString(),
            Usage = SubscriptionFeatureUsage.Comparison.ToString(),
            DisplayOrder = 0
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var createDto = new CreateSubscriptionFeatureDto
        {
            NameKey = Feature.NameKey!,
            Type = Enum.Parse<SubscriptionFeatureType>(Feature.Type!),
            Usage = Enum.Parse<SubscriptionFeatureUsage>(Feature.Usage!),
            DisplayOrder = Feature.DisplayOrder
        };

        // Set optional fields only when they have values
        // (Protobuf setters throw ArgumentNullException for null values)
        if (!string.IsNullOrEmpty(Feature.DescriptionKey))
        {
            createDto.DescriptionKey = Feature.DescriptionKey;
        }

        await _featureService.CreateFeatureAsync(createDto);
        return NoContent();
    }

    public class CreateFeatureInput
    {
        [Required]
        [MaxLength(256)]
        [Display(Name = "NameKey")]
        public string? NameKey { get; set; }

        [MaxLength(256)]
        [Display(Name = "DescriptionKey")]
        public string? DescriptionKey { get; set; }

        [Required]
        [Display(Name = "FeatureType")]
        public string? Type { get; set; }

        [Required]
        [Display(Name = "Usage")]
        public string? Usage { get; set; }

        [Display(Name = "DisplayOrder")]
        public int DisplayOrder { get; set; }
    }
}
