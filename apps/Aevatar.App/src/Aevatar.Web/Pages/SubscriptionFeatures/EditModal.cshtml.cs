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

[Authorize(Policy = SubscriptionProductPermissions.Features.Update)]
public class EditModalModel : AevatarPageModel
{
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; }

    [BindProperty]
    public EditFeatureInput Feature { get; set; } = new();

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

    public EditModalModel(ISubscriptionFeatureService featureService)
    {
        _featureService = featureService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var feature = await _featureService.GetFeatureAsync(Id);
        if (feature == null) return NotFound();

        Feature = new EditFeatureInput
        {
            NameKey = feature.NameKey,
            DescriptionKey = feature.DescriptionKey,
            Type = feature.Type.ToString(),
            Usage = feature.Usage?.ToString() ?? SubscriptionFeatureUsage.Comparison.ToString(),
            DisplayOrder = feature.DisplayOrder
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var updateDto = new UpdateSubscriptionFeatureDto
        {
            NameKey = Feature.NameKey!,
            Type = Enum.Parse<SubscriptionFeatureType>(Feature.Type!),
            Usage = Enum.Parse<SubscriptionFeatureUsage>(Feature.Usage!),
            DescriptionKey = Feature.DescriptionKey ?? string.Empty,
            DisplayOrder = Feature.DisplayOrder
        };

        await _featureService.UpdateFeatureAsync(Id, updateDto);
        return NoContent();
    }

    public class EditFeatureInput
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
