using Volo.Abp.Application.Dtos;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Input DTO for querying languages with pagination and filtering.
/// </summary>
public class GetLanguagesInput : PagedAndSortedResultRequestDto
{
    public override string Sorting { get; set; } = "CultureName";

    /// <summary>
    /// Filter text for searching.
    /// </summary>
    public string? Filter { get; set; }
}

