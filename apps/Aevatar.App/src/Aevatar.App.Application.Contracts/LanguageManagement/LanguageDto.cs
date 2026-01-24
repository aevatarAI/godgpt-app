using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Data transfer object for Language entity.
/// </summary>
public class LanguageDto : EntityDto<Guid>
{
    /// <summary>
    /// Culture name (e.g., "en", "zh-Hans").
    /// </summary>
    public string CultureName { get; set; } = string.Empty;

    /// <summary>
    /// UI culture name (e.g., "en", "zh-Hans").
    /// </summary>
    public string UiCultureName { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the language.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Flag icon identifier.
    /// </summary>
    public string? FlagIcon { get; set; }

    /// <summary>
    /// Indicates whether this language is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Indicates whether this is the default language.
    /// </summary>
    //public bool IsDefaultLanguage { get; set; }

    /// <summary>
    /// Concurrency stamp for optimistic concurrency control.
    /// </summary>
    public string? ConcurrencyStamp { get; set; }

    /// <summary>
    /// Creation time.
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Creator user ID.
    /// </summary>
    public Guid? CreatorId { get; set; }

    /// <summary>
    /// Extra properties.
    /// </summary>
    public Dictionary<string, object>? ExtraProperties { get; set; }
}

