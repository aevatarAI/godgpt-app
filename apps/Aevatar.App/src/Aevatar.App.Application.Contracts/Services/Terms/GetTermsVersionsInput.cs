using Volo.Abp.Application.Dtos;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// Input for querying Terms of Service versions.
/// </summary>
public class GetTermsVersionsInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// Filter by version number (partial match).
    /// </summary>
    public string? VersionFilter { get; set; }

    /// <summary>
    /// Filter by active status.
    /// </summary>
    public bool? IsActiveFilter { get; set; }

    /// <summary>
    /// Filter by language code.
    /// </summary>
    public string? LanguageFilter { get; set; }
}
