using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Localization;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace Aevatar.Web.Pages.SubscriptionFeatures;

[Authorize(Policy = SubscriptionProductPermissions.Features.Default)]
public class IndexModel : AevatarPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? TypeFilter { get; set; }

    public IReadOnlyList<FeatureListItem> Features { get; private set; } = new List<FeatureListItem>();

    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanReorder { get; private set; }

    public List<SelectListItem> TypeOptions { get; } = new()
    {
        new SelectListItem("All Types", ""),
        new SelectListItem("None", SubscriptionFeatureType.None.ToString()),
        new SelectListItem("Core Feature", SubscriptionFeatureType.Core.ToString()),
        new SelectListItem("Advanced Feature", SubscriptionFeatureType.Advanced.ToString())
    };

    private readonly ISubscriptionFeatureService _featureService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IStringLocalizer<AevatarResource> _localizer;

    public IndexModel(
        ISubscriptionFeatureService featureService,
        IAuthorizationService authorizationService,
        IStringLocalizer<AevatarResource> localizer)
    {
        _featureService = featureService;
        _authorizationService = authorizationService;
        _localizer = localizer;
    }

    public async Task OnGetAsync()
    {
        // Check permissions
        CanCreate = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Features.Create)).Succeeded;
        CanEdit = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Features.Update)).Succeeded;
        CanDelete = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Features.Delete)).Succeeded;
        CanReorder = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Features.Reorder)).Succeeded;

        // Parse type filter
        SubscriptionFeatureType? type = null;
        if (!string.IsNullOrEmpty(TypeFilter) && Enum.TryParse<SubscriptionFeatureType>(TypeFilter, out var t))
        {
            type = t;
        }

        // Fetch features
        var features = await _featureService.GetAllFeaturesAdminAsync(type);

        // Map to view models
        Features = features.OrderBy(f => f.DisplayOrder).Select(f => new FeatureListItem
        {
            Id = f.Id,
            NameKey = f.NameKey,
            Name = f.Name,
            DescriptionKey = f.DescriptionKey,
            Description = f.Description,
            Type = f.Type,
            TypeName = f.TypeName,
            Usage = f.Usage,
            DisplayOrder = f.DisplayOrder,
            CreatedAt = f.CreatedAt
        }).ToList();
    }

    public class FeatureListItem
    {
        public string Id { get; set; }
        public string NameKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? DescriptionKey { get; set; }
        public string? Description { get; set; }
        public SubscriptionFeatureType Type { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public SubscriptionFeatureUsage? Usage { get; set; }
        public int DisplayOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
