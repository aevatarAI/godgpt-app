using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.App.LanguageManagement;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.Languages;

[Authorize(Policy = AppPermissions.LanguageManagement.Languages.Create)]
public class CreateModalModel : AevatarPageModel
{
    [BindProperty]
    public CreateLanguageViewModel Language { get; set; } = new();

    private readonly ILanguageAppService _languageAppService;

    public CreateModalModel(ILanguageAppService languageAppService)
    {
        _languageAppService = languageAppService;
    }

    public void OnGet()
    {
        Language = new CreateLanguageViewModel
        {
            IsEnabled = true
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _languageAppService.CreateAsync(new CreateLanguageDto
        {
            DisplayName = Language.DisplayName,
            CultureName = Language.CultureName,
            UiCultureName = Language.UiCultureName,
            FlagIcon = Language.FlagIcon,
            IsEnabled = Language.IsEnabled
        });

        return NoContent();
    }

    public class CreateLanguageViewModel
    {
        [Required]
        [MaxLength(32)]
        [Display(Name = "DisplayName")]
        public string? DisplayName { get; set; }

        [Required]
        [MaxLength(10)]
        [Display(Name = "CultureName")]
        public string? CultureName { get; set; }

        [MaxLength(10)]
        [Display(Name = "UiCultureName")]
        public string? UiCultureName { get; set; }

        [MaxLength(48)]
        [Display(Name = "FlagIcon")]
        public string? FlagIcon { get; set; }

        [Display(Name = "IsEnabled")]
        public bool IsEnabled { get; set; }
    }
}

