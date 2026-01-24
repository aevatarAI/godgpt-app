using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.App.LanguageManagement;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.Languages;

[Authorize(Policy = AppPermissions.LanguageManagement.Languages.Edit)]
public class EditModalModel : AevatarPageModel
{
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public EditLanguageViewModel Language { get; set; } = new();

    private readonly ILanguageAppService _languageAppService;

    public EditModalModel(ILanguageAppService languageAppService)
    {
        _languageAppService = languageAppService;
    }

    public async Task OnGetAsync()
    {
        var dto = await _languageAppService.GetAsync(Id);
        Language = new EditLanguageViewModel
        {
            DisplayName = dto.DisplayName,
            FlagIcon = dto.FlagIcon,
            IsEnabled = dto.IsEnabled
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _languageAppService.UpdateAsync(Id, new UpdateLanguageDto
        {
            DisplayName = Language.DisplayName,
            FlagIcon = Language.FlagIcon,
            IsEnabled = Language.IsEnabled
        });

        return NoContent();
    }

    public class EditLanguageViewModel
    {
        [Required]
        [MaxLength(32)]
        [Display(Name = "DisplayName")]
        public string? DisplayName { get; set; }

        [MaxLength(48)]
        [Display(Name = "FlagIcon")]
        public string? FlagIcon { get; set; }

        [Display(Name = "IsEnabled")]
        public bool IsEnabled { get; set; }
    }
}

