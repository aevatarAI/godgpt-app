using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.App.LanguageManagement;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Web.Pages.LanguageTexts;

[Authorize(Policy = AppPermissions.LanguageManagement.LanguageTexts.Default)]
public class IndexModel : AevatarPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ResourceName { get; set; } = "Aevatar";

    [BindProperty(SupportsGet = true)]
    public string? BaseCultureName { get; set; } = "en";

    [BindProperty(SupportsGet = true)]
    public string? TargetCultureName { get; set; } = "zh-Hans";

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool GetOnlyEmptyValues { get; set; }

    [BindProperty(SupportsGet = true)]
    public int CurrentPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    public IReadOnlyList<LanguageTextDto> LanguageTexts { get; private set; } = new List<LanguageTextDto>();
    public IReadOnlyList<LanguageResourceDto> Resources { get; private set; } = new List<LanguageResourceDto>();
    public IReadOnlyList<CultureInfoDto> Cultures { get; private set; } = new List<CultureInfoDto>();
    public long TotalCount { get; private set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    
    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanRestore { get; private set; }

    private readonly ILanguageTextAppService _languageTextAppService;
    private readonly ILanguageAppService _languageAppService;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(
        ILanguageTextAppService languageTextAppService,
        ILanguageAppService languageAppService,
        IAuthorizationService authorizationService)
    {
        _languageTextAppService = languageTextAppService;
        _languageAppService = languageAppService;
        _authorizationService = authorizationService;
    }

    public async Task OnGetAsync()
    {
        // Check permissions
        CanCreate = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.LanguageTexts.Create)).Succeeded;
        CanEdit = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.LanguageTexts.Edit)).Succeeded;
        CanRestore = (await _authorizationService.AuthorizeAsync(User, AppPermissions.LanguageManagement.LanguageTexts.Restore)).Succeeded;
        
        // Ensure CurrentPage is at least 1
        if (CurrentPage < 1) CurrentPage = 1;

        // Load resources and cultures for dropdown
        Resources = await _languageAppService.GetResourcesAsync();
        Cultures = await _languageAppService.GetCultureListAsync();

        // Query language texts
        if (!string.IsNullOrEmpty(BaseCultureName) && !string.IsNullOrEmpty(TargetCultureName))
        {
            var result = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                BaseCultureName = BaseCultureName,
                TargetCultureName = TargetCultureName,
                Filter = Filter,
                GetOnlyEmptyValues = GetOnlyEmptyValues,
                SkipCount = (CurrentPage - 1) * PageSize,
                MaxResultCount = PageSize
            });
            LanguageTexts = result.Items.ToList();
            TotalCount = result.TotalCount;
        }
    }

    public string GetPageUrl(int page)
    {
        var queryParams = new List<string>
        {
            $"CurrentPage={page}",
            $"PageSize={PageSize}"
        };

        if (!string.IsNullOrEmpty(ResourceName))
            queryParams.Add($"ResourceName={Uri.EscapeDataString(ResourceName)}");
        if (!string.IsNullOrEmpty(BaseCultureName))
            queryParams.Add($"BaseCultureName={Uri.EscapeDataString(BaseCultureName)}");
        if (!string.IsNullOrEmpty(TargetCultureName))
            queryParams.Add($"TargetCultureName={Uri.EscapeDataString(TargetCultureName)}");
        if (!string.IsNullOrEmpty(Filter))
            queryParams.Add($"Filter={Uri.EscapeDataString(Filter)}");
        if (GetOnlyEmptyValues)
            queryParams.Add("GetOnlyEmptyValues=true");

        return $"?{string.Join("&", queryParams)}";
    }
}
