using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aevatar.Web.Pages.SubscriptionProducts;

[Authorize(Policy = SubscriptionProductPermissions.Products.Default)]
public class IndexModel : AevatarPageModel
{
    [BindProperty(SupportsGet = true)]
    public PaymentPlatform? PlatformFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? IsListedFilter { get; set; }

    public IReadOnlyList<SubscriptionProductAdminDto> Products { get; private set; } = new List<SubscriptionProductAdminDto>();

    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanSetListed { get; private set; }
    public bool CanSyncPrice { get; private set; }
    public bool CanSetPrice { get; private set; }
    public bool CanDeletePrice { get; private set; }

    public List<SelectListItem> PlatformOptions { get; private set; } = new();

    public List<SelectListItem> ListedStatusOptions { get; } = new()
    {
        new SelectListItem("All Statuses", ""),
        new SelectListItem("Listed", "true"),
        new SelectListItem("Unlisted", "false")
    };

    private readonly ISubscriptionProductService _productService;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(
        ISubscriptionProductService productService,
        IAuthorizationService authorizationService)
    {
        _productService = productService;
        _authorizationService = authorizationService;
    }

    public async Task OnGetAsync()
    {
        // Initialize platform options
        PlatformOptions = new List<SelectListItem> { new SelectListItem("All Platforms", "") };
        PlatformOptions.AddRange(Enum.GetValues<PaymentPlatform>()
            .Select(p => new SelectListItem(GetPlatformDisplayName(p), ((int)p).ToString())));

        // Check permissions
        CanCreate = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Products.Create)).Succeeded;
        CanEdit = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Products.Update)).Succeeded;
        CanDelete = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Products.Delete)).Succeeded;
        CanSetListed = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Products.SetListed)).Succeeded;
        CanSyncPrice = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Prices.SyncPrice)).Succeeded;
        CanSetPrice = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Prices.Set)).Succeeded;
        CanDeletePrice = (await _authorizationService.AuthorizeAsync(User, SubscriptionProductPermissions.Prices.Delete)).Succeeded;

        // Fetch products directly from service (already contains localized data)
        Products = await _productService.GetAllProductsAdminAsync(PlatformFilter, IsListedFilter);
    }

    public string GetPlatformDisplayName(PaymentPlatform platform)
    {
        return platform switch
        {
            PaymentPlatform.Stripe => "Stripe",
            PaymentPlatform.AppStore => "Apple",
            PaymentPlatform.GooglePlay => "Google",
            _ => platform.ToString()
        };
    }
}
