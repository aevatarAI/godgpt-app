using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Localization;
using Volo.Abp.Threading;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Localization contributor that reads language texts from database.
/// Uses LanguageText entity to store and retrieve localized strings.
/// </summary>
public class DbLocalizationContributor : ILocalizationResourceContributor
{
    public bool IsDynamic => true;

    private LocalizationResourceBase _resource = default!;
    private IRepository<LanguageText, Guid> _languageTextRepository = default!;
    private IDistributedCache<LanguageTextCacheItem> _distributedCache = default!;
    private ILogger<DbLocalizationContributor> _logger = default!;

    public void Initialize(LocalizationResourceInitializationContext context)
    {
        _resource = context.Resource;
        _languageTextRepository = context.ServiceProvider.GetRequiredService<IRepository<LanguageText, Guid>>();
        _distributedCache = context.ServiceProvider.GetRequiredService<IDistributedCache<LanguageTextCacheItem>>();
        _logger = context.ServiceProvider.GetService<ILogger<DbLocalizationContributor>>()
                  ?? NullLogger<DbLocalizationContributor>.Instance;
    }

    public virtual LocalizedString? GetOrNull(string cultureName, string name)
    {
        return GetOrNullInternal(_resource.ResourceName, name, cultureName);
    }

    protected virtual LocalizedString? GetOrNullInternal(string resourceName, string name, string cultureName)
    {
        var texts = AsyncHelper.RunSync(() => GetResourceTextsAsync(resourceName, cultureName));
        if (texts == null || texts.Count == 0)
        {
            return null;
        }

        if (texts.TryGetValue(name, out var value))
        {
            return new LocalizedString(name, value);
        }

        return null;
    }

    public virtual void Fill(string cultureName, Dictionary<string, LocalizedString> dictionary)
    {
        FillInternal(_resource.ResourceName, cultureName, dictionary);
    }

    protected virtual void FillInternal(string resourceName, string cultureName, Dictionary<string, LocalizedString> dictionary)
    {
        var texts = AsyncHelper.RunSync(() => GetResourceTextsAsync(resourceName, cultureName));
        if (texts == null)
        {
            return;
        }

        foreach (var keyValue in texts)
        {
            dictionary[keyValue.Key] = new LocalizedString(keyValue.Key, keyValue.Value);
        }
    }

    public virtual async Task FillAsync(string cultureName, Dictionary<string, LocalizedString> dictionary)
    {
        await FillInternalAsync(_resource.ResourceName, cultureName, dictionary);
    }

    protected virtual async Task FillInternalAsync(string resourceName, string cultureName, Dictionary<string, LocalizedString> dictionary)
    {
        var texts = await GetResourceTextsAsync(resourceName, cultureName);
        if (texts == null)
        {
            return;
        }

        foreach (var keyValue in texts)
        {
            dictionary[keyValue.Key] = new LocalizedString(keyValue.Key, keyValue.Value);
        }
    }

    public virtual Task<IEnumerable<string>> GetSupportedCulturesAsync()
    {
        return GetSupportedCulturesFromDatabaseAsync();
    }

    /// <summary>
    /// Gets resource texts from database asynchronously.
    /// </summary>
    protected virtual async Task<Dictionary<string, string>?> GetResourceTextsAsync(string resourceName, string cultureName)
    {
        try
        {
            var languageTextCacheItem = await _distributedCache.GetOrAddAsync(
                LanguageTextCacheItem.CalculateCacheKey(resourceName, cultureName), async () =>
                {
                    var queryable = await _languageTextRepository.GetQueryableAsync();
                    var texts = queryable
                        .Where(x => x.ResourceName == resourceName && x.CultureName == cultureName)
                        .Select(x => new { x.Name, x.Value })
                        .ToList();

                    if (texts.Count == 0)
                    {
                        _logger.LogDebug("No localization texts found for resource '{ResourceName}' and culture '{CultureName}'", 
                            resourceName, cultureName);
                        return new LanguageTextCacheItem();
                    }

                    return new LanguageTextCacheItem
                    {
                        Dictionary = texts.ToDictionary(x => x.Name, x => x.Value)
                    };
                });
            return languageTextCacheItem?.Dictionary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get localization texts for resource '{ResourceName}' and culture '{CultureName}'", 
                resourceName, cultureName);
            return null;
        }
    }

    /// <summary>
    /// Gets all supported cultures from database.
    /// </summary>
    protected virtual async Task<IEnumerable<string>> GetSupportedCulturesFromDatabaseAsync()
    {
        try
        {
            var queryable = await _languageTextRepository.GetQueryableAsync();
            var cultures = queryable
                .Where(x => x.ResourceName == _resource.ResourceName)
                .Select(x => x.CultureName)
                .Distinct()
                .ToList();

            return cultures;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get supported cultures for resource '{ResourceName}'", _resource.ResourceName);
            return Array.Empty<string>();
        }
    }
}
