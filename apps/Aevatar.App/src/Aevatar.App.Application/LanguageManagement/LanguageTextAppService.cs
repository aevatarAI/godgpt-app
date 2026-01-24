using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Application service for language text management.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize(AppPermissions.LanguageManagement.LanguageTexts.Default)]
public class LanguageTextAppService : AppAppService, ILanguageTextAppService
{
    private readonly IRepository<LanguageText, Guid> _languageTextRepository;
    private readonly IAbpApplicationLocalizationAppService _localizationAppService;
    private readonly IDistributedCache<LanguageTextCacheItem> _distributedCache;
    private readonly IGuidGenerator _guidGenerator;

    public LanguageTextAppService(
        IRepository<LanguageText, Guid> languageTextRepository,
        IAbpApplicationLocalizationAppService localizationAppService,
        IGuidGenerator guidGenerator, 
        IDistributedCache<LanguageTextCacheItem> distributedCache)
    {
        _languageTextRepository = languageTextRepository;
        _localizationAppService = localizationAppService;
        _guidGenerator = guidGenerator;
        _distributedCache = distributedCache;
    }

    /// <inheritdoc />
    public async Task<PagedResultDto<LanguageTextDto>> GetListAsync(GetLanguageTextsInput input)
    {
        // Get base culture texts
        var baseTexts = await GetCultureTextsAsync(input.BaseCultureName!, input.ResourceName);

        // Get target culture texts
        var targetTexts = await GetCultureTextsAsync(input.TargetCultureName!, input.ResourceName);

        // Create dictionary for target texts lookup (O(1) lookup)
        var targetTextDict = targetTexts.ToDictionary(
            t => (t.ResourceName, t.Name),
            t => t.Value);

        // Left join: base texts as primary, target texts as secondary
        var joinedQuery = baseTexts.Select(baseText => new LanguageTextDto
        {
            ResourceName = baseText.ResourceName,
            CultureName = input.TargetCultureName!,
            BaseCultureName = input.BaseCultureName!,
            BaseValue = baseText.Value,
            Name = baseText.Name,
            Value = targetTextDict.TryGetValue((baseText.ResourceName, baseText.Name), out var targetValue)
                ? targetValue
                : string.Empty
        }).AsQueryable();

        // Apply Filter on merged results (Name, BaseValue, Value)
        joinedQuery = joinedQuery
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(),
                x => x.Name.Contains(input.Filter!) ||
                     x.BaseValue.Contains(input.Filter!) ||
                     x.Value.Contains(input.Filter!));

        // Apply GetOnlyEmptyValues filter
        joinedQuery = joinedQuery
            .WhereIf(input.GetOnlyEmptyValues, x => string.IsNullOrEmpty(x.Value));

        // Get total count
        var totalCount = joinedQuery.Count();

        // Apply sorting and pagination
        var languageTexts = joinedQuery
            .OrderBy(input.Sorting.IsNullOrWhiteSpace() ? "Name" : input.Sorting)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new PagedResultDto<LanguageTextDto>(totalCount, languageTexts);
    }

    /// <inheritdoc />
    public async Task<LanguageTextDto> GetAsync(GetLanguageTextInput input)
    {
        // Get base culture text
        var baseTexts = await GetCultureTextsAsync(input.BaseCultureName, input.ResourceName);
        var baseText = baseTexts.FirstOrDefault(x => x.Name == input.Name);
        var baseValue = baseText.Value ?? string.Empty;

        // Get target culture text
        var targetTexts = await GetCultureTextsAsync(input.CultureName, input.ResourceName);
        var targetText = targetTexts.FirstOrDefault(x => x.Name == input.Name);
        var targetValue = targetText.Value ?? string.Empty;

        return new LanguageTextDto
        {
            ResourceName = input.ResourceName,
            CultureName = input.CultureName,
            BaseCultureName = input.BaseCultureName,
            BaseValue = baseValue,
            Name = input.Name,
            Value = targetValue
        };
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.LanguageManagement.LanguageTexts.Edit)]
    public async Task AddOrUpdateAsync(UpdateLanguageTextInput input)
    {
        // Find existing language text
        var existingText = await _languageTextRepository.FindAsync(x => 
            x.ResourceName == input.ResourceName &&
            x.CultureName == input.CultureName &&
            x.Name == input.Name);

        if (existingText == null)
        {
            // Create new language text
            var newText = new LanguageText(
                _guidGenerator.Create(),
                input.ResourceName,
                input.CultureName,
                input.Name,
                input.Value);
            await _languageTextRepository.InsertAsync(newText, autoSave: true);
        }
        else
        {
            // Update existing language text
            existingText.Value = input.Value;
            await _languageTextRepository.UpdateAsync(existingText, autoSave: true);
        }

        await UpdateLanguageTextCacheItemAsync(input.ResourceName, input.CultureName, input.Name, input.Value);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.LanguageManagement.LanguageTexts.Create)]
    public async Task AddAsync(CreateLanguageTextInput input)
    {
        // Find existing language text
        var existingText = await _languageTextRepository.FindAsync(x => 
            x.ResourceName == input.ResourceName &&
            x.CultureName == input.CultureName &&
            x.Name == input.Name);

        if (existingText != null)
        {
            throw new UserFriendlyException(L["LanguageTextAlreadyExists", input.ResourceName, input.CultureName, input.Name]);
        }

        // Create new language text
        var newText = new LanguageText(
            _guidGenerator.Create(),
            input.ResourceName,
            input.CultureName,
            input.Name,
            input.Value);
        await _languageTextRepository.InsertAsync(newText, autoSave: true);

        await UpdateLanguageTextCacheItemAsync(input.ResourceName, input.CultureName, input.Name, input.Value);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.LanguageManagement.LanguageTexts.Restore)]
    public async Task RestoreAsync(RestoreLanguageTextInput input)
    {
        var languageText = await _languageTextRepository.FindAsync(x => 
            x.ResourceName == input.ResourceName && 
            x.CultureName == input.CultureName && 
            x.Name == input.Name);

        if (languageText == null)
        {
            throw new UserFriendlyException(L["LanguageTextNotFound", input.ResourceName, input.CultureName, input.Name]);
        }

        await _languageTextRepository.DeleteAsync(languageText.Id, autoSave: true);
        
        var languageTextCacheItem = await _distributedCache.GetAsync(
            LanguageTextCacheItem.CalculateCacheKey(input.ResourceName, input.CultureName));
        if (languageTextCacheItem != null)
        {
            languageTextCacheItem.Dictionary.Remove(input.Name);
            await _distributedCache.SetAsync(
                LanguageTextCacheItem.CalculateCacheKey(input.ResourceName, input.CultureName),
                languageTextCacheItem);
        }
    }
    
    /// <summary>
    /// Get texts for a specific culture by localization service.
    /// </summary>
    private async Task<List<(string ResourceName, string Name, string Value)>> GetCultureTextsAsync(
        string cultureName,
        string? resourceName)
    {
        var result = new Dictionary<(string ResourceName, string Name), string>();
        
        var localizationResult = await _localizationAppService.GetAsync(
            new ApplicationLocalizationRequestDto
            {
                CultureName = cultureName,
                OnlyDynamics = false
            });

        if (localizationResult?.Resources != null)
        {
            foreach (var resource in localizationResult.Resources)
            {
                // Filter by resource name if specified
                if (!resourceName.IsNullOrWhiteSpace() && resource.Key != resourceName)
                {
                    continue;
                }

                if (resource.Value?.Texts != null)
                {
                    foreach (var text in resource.Value.Texts)
                    {
                        result[(resource.Key, text.Key)] = text.Value;
                    }
                }
            }
        }

        return result.Select(x => (x.Key.ResourceName, x.Key.Name, x.Value)).ToList();
    }
    
    private async Task UpdateLanguageTextCacheItemAsync(string resourceName, string cultureName, string name, string value)
    {
        var languageTextCacheItem = await _distributedCache.GetAsync(
            LanguageTextCacheItem.CalculateCacheKey(resourceName, cultureName));
        if (languageTextCacheItem != null)
        {
            languageTextCacheItem.Dictionary[name] = value;
            await _distributedCache.SetAsync(
                LanguageTextCacheItem.CalculateCacheKey(resourceName, cultureName),
                languageTextCacheItem);
        }
    }
}
