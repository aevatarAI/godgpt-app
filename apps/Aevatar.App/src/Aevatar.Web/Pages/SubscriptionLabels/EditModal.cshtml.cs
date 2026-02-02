using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.SubscriptionLabels;

[Authorize(Policy = SubscriptionProductPermissions.Labels.Update)]
public class EditModalModel : AevatarPageModel
{
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; }

    [BindProperty]
    public EditLabelInput Label { get; set; } = new();

    private readonly ISubscriptionLabelService _labelService;

    public EditModalModel(ISubscriptionLabelService labelService)
    {
        _labelService = labelService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var label = await _labelService.GetLabelAsync(Id);
        if (label == null) return NotFound();

        Label = new EditLabelInput
        {
            Key = label.NameKey
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var updateDto = new UpdateSubscriptionLabelDto
        {
            NameKey = Label.Key!
        };

        await _labelService.UpdateLabelAsync(Id, updateDto);
        return NoContent();
    }

    public class EditLabelInput
    {
        [Required]
        [MaxLength(128)]
        [RegularExpression(@"^[a-z][a-z0-9_]*$", ErrorMessage = "Key must be snake_case (lowercase letters, numbers, and underscores)")]
        [Display(Name = "LabelKey")]
        public string? Key { get; set; }
    }
}
