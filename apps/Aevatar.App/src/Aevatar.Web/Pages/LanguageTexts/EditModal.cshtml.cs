using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Aevatar.App.LanguageManagement;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.LanguageTexts;

[Authorize(Policy = AppPermissions.LanguageManagement.LanguageTexts.Edit)]
public class EditModalModel : AevatarPageModel
{
    [BindProperty(SupportsGet = true)]
    public string ResourceName { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string CultureName { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Name { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string BaseCultureName { get; set; } = "en";

    [BindProperty]
    public EditLanguageTextViewModel LanguageText { get; set; } = new();
    
    public bool CanEdit { get; private set; }
    public bool CanRestore { get; private set; }

    private readonly ILanguageTextAppService _languageTextAppService;
    private readonly IAuthorizationService _authorizationService;

    public EditModalModel(
        ILanguageTextAppService languageTextAppService,
        IAuthorizationService authorizationService)
    {
        _languageTextAppService = languageTextAppService;
        _authorizationService = authorizationService;
    }

    public async Task OnGetAsync()
    {
        // Check permissions
        CanEdit = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.LanguageTexts.Edit)).Succeeded;
        CanRestore = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.LanguageTexts.Restore)).Succeeded;
        
        var dto = await _languageTextAppService.GetAsync(new GetLanguageTextInput
        {
            ResourceName = ResourceName,
            Name = Name,
            CultureName = CultureName,
            BaseCultureName = BaseCultureName
        });

        LanguageText = new EditLanguageTextViewModel
        {
            ResourceName = dto.ResourceName,
            CultureName = dto.CultureName,
            Name = dto.Name,
            BaseValue = dto.BaseValue,
            Value = dto.Value
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _languageTextAppService.AddOrUpdateAsync(new UpdateLanguageTextInput
        {
            ResourceName = LanguageText.ResourceName!,
            CultureName = LanguageText.CultureName!,
            Name = LanguageText.Name!,
            Value = LanguageText.Value ?? string.Empty
        });

        return NoContent();
    }

    public async Task<IActionResult> OnPostRestoreAsync()
    {
        await _languageTextAppService.RestoreAsync(new RestoreLanguageTextInput
        {
            ResourceName = LanguageText.ResourceName!,
            CultureName = LanguageText.CultureName!,
            Name = LanguageText.Name!
        });

        return NoContent();
    }

    public class EditLanguageTextViewModel
    {
        public string? ResourceName { get; set; }
        public string? CultureName { get; set; }
        public string? Name { get; set; }
        public string? BaseValue { get; set; }

        [Display(Name = "Value")]
        public string? Value { get; set; }
    }
}
