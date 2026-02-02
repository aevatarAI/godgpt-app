using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.SubscriptionLabels;

[Authorize(Policy = SubscriptionProductPermissions.Labels.Create)]
public class CreateModalModel : AevatarPageModel
{
    [BindProperty]
    public CreateLabelInput Label { get; set; } = new();

    private readonly ISubscriptionLabelService _labelService;

    public CreateModalModel(ISubscriptionLabelService labelService)
    {
        _labelService = labelService;
    }

    public void OnGet()
    {
        Label = new CreateLabelInput();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var createDto = new CreateSubscriptionLabelDto
        {
            NameKey = Label.Key!
        };

        await _labelService.CreateLabelAsync(createDto);
        return NoContent();
    }

    public class CreateLabelInput
    {
        [Required]
        [MaxLength(128)]
        [RegularExpression(@"^[a-z][a-z0-9_]*$", ErrorMessage = "Key must be snake_case (lowercase letters, numbers, and underscores)")]
        [Display(Name = "LabelKey")]
        public string? Key { get; set; }
    }
}
