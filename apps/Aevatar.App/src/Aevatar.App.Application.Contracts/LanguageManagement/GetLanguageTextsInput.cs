using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Input for getting language texts with pagination and filtering.
/// </summary>
public class GetLanguageTextsInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// Filter text to search in name or value.
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// Resource name to filter by.
    /// </summary>
    public string? ResourceName { get; set; }

    /// <summary>
    /// Base culture name (e.g., "en").
    /// </summary>
    [Required]
    public string? BaseCultureName { get; set; }

    /// <summary>
    /// Target culture name (e.g., "zh-Hans").
    /// </summary>
    [Required]
    public string? TargetCultureName { get; set; }

    /// <summary>
    /// If true, only returns items where target value is empty.
    /// </summary>
    public bool GetOnlyEmptyValues { get; set; }
}

