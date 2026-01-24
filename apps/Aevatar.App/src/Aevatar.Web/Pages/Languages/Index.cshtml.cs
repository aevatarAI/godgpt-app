using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.App.LanguageManagement;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.Languages;

[Authorize(Policy = AppPermissions.LanguageManagement.Languages.Default)]
public class IndexModel : AevatarPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public IReadOnlyList<LanguageDto> Languages { get; private set; } = new List<LanguageDto>();
    
    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }

    private readonly ILanguageAppService _languageAppService;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(
        ILanguageAppService languageAppService,
        IAuthorizationService authorizationService)
    {
        _languageAppService = languageAppService;
        _authorizationService = authorizationService;
    }

    public async Task OnGetAsync()
    {
        // Check permissions
        CanCreate = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.Languages.Create)).Succeeded;
        CanEdit = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.Languages.Edit)).Succeeded;
        CanDelete = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.Languages.Delete)).Succeeded;
        
        var result = await _languageAppService.GetListAsync(new GetLanguagesInput
        {
            Filter = Filter,
            MaxResultCount = 100
        });
        Languages = result.Items.ToList();
    }
}
