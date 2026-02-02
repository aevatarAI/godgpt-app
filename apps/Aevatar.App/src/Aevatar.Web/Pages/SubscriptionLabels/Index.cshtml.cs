using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.App.Localization;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;

namespace Aevatar.Web.Pages.SubscriptionLabels;

[Authorize(Policy = SubscriptionProductPermissions.Labels.Default)]
public class IndexModel : AevatarPageModel
{
    public IReadOnlyList<LabelListItem> Labels { get; private set; } = new List<LabelListItem>();

    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }

    private readonly ISubscriptionLabelService _labelService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IStringLocalizer<AevatarResource> _localizer;

    public IndexModel(
        ISubscriptionLabelService labelService,
        IAuthorizationService authorizationService,
        IStringLocalizer<AevatarResource> localizer)
    {
        _labelService = labelService;
        _authorizationService = authorizationService;
        _localizer = localizer;
    }

    public async Task OnGetAsync()
    {
        // Check permissions
        CanCreate = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Labels.Create)).Succeeded;
        CanEdit = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Labels.Update)).Succeeded;
        CanDelete = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Labels.Delete)).Succeeded;

        // Fetch labels
        var labels = await _labelService.GetAllLabelsAsync();

        // Map to view models
        Labels = labels.Select(l => new LabelListItem
        {
            Id = l.Id,
            Key = l.NameKey,
            Name = l.Name,
            CreatedAt = l.CreatedAt
        }).ToList();
    }

    public class LabelListItem
    {
        public string Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
