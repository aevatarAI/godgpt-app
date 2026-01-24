using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.App.LanguageManagement;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.LanguageTexts;

[Authorize(Policy = AppPermissions.LanguageManagement.LanguageTexts.Create)]
public class CreateModalModel : AevatarPageModel
{
    [BindProperty]
    public CreateLanguageTextViewModel LanguageText { get; set; } = new();

    public IReadOnlyList<LanguageResourceDto> Resources { get; private set; } = new List<LanguageResourceDto>();
    public IReadOnlyList<CultureInfoDto> Cultures { get; private set; } = new List<CultureInfoDto>();

    private readonly ILanguageTextAppService _languageTextAppService;
    private readonly ILanguageAppService _languageAppService;

    public CreateModalModel(
        ILanguageTextAppService languageTextAppService,
        ILanguageAppService languageAppService)
    {
        _languageTextAppService = languageTextAppService;
        _languageAppService = languageAppService;
    }

    public async Task OnGetAsync()
    {
        Resources = await _languageAppService.GetResourcesAsync();
        Cultures = await _languageAppService.GetCultureListAsync();
        
        LanguageText = new CreateLanguageTextViewModel
        {
            ResourceName = "Aevatar"
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _languageTextAppService.AddAsync(new CreateLanguageTextInput
        {
            ResourceName = LanguageText.ResourceName!,
            CultureName = LanguageText.CultureName!,
            Name = LanguageText.Name!,
            Value = LanguageText.Value!
        });

        return NoContent();
    }

    public class CreateLanguageTextViewModel
    {
        [Required]
        [Display(Name = "ResourceName")]
        public string? ResourceName { get; set; }

        [Required]
        [Display(Name = "CultureName")]
        public string? CultureName { get; set; }

        [Required]
        [Display(Name = "Key")]
        public string? Name { get; set; }

        [Required]
        [Display(Name = "Value")]
        public string? Value { get; set; }
    }
}
