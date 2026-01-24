using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Localization;
using Volo.Abp.SettingManagement;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Application service for language management.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize(AppPermissions.LanguageManagement.Languages.Default)]
public class LanguageAppService : AppAppService, ILanguageAppService
{
    private readonly IRepository<Language, Guid> _languageRepository;
    private readonly AbpLocalizationOptions _abpLocalizationOptions; 
    private readonly ISettingManager _settingManager;    
    private readonly IGuidGenerator _guidGenerator;

    public LanguageAppService(
        IRepository<Language, Guid> languageRepository,
        IGuidGenerator guidGenerator,
        IOptions<AbpLocalizationOptions> abpLocalizationOptions,
        ISettingManager settingManager)
    {
        _languageRepository = languageRepository;
        _abpLocalizationOptions = abpLocalizationOptions.Value;
        _guidGenerator = guidGenerator;
        _settingManager = settingManager;
    }

    /// <inheritdoc />
    public async Task<PagedResultDto<LanguageDto>> GetListAsync(GetLanguagesInput input)
    {
        var queryable = await _languageRepository.GetQueryableAsync();

        // Apply filters
        queryable = queryable
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(),
                x => x.CultureName.Contains(input.Filter!) || x.DisplayName.Contains(input.Filter!));

        // Get total count
        var totalCount = await AsyncExecuter.CountAsync(queryable);

        // Apply sorting and pagination
        var languages = await AsyncExecuter.ToListAsync(
            queryable.OrderBy(input.Sorting).PageBy(input)
        );

        return new PagedResultDto<LanguageDto>(totalCount, ObjectMapper.Map<List<Language>, List<LanguageDto>>(languages));
    }

    /// <inheritdoc />
    public async Task<ListResultDto<LanguageDto>> GetAllListAsync()
    {
        var languages = await _languageRepository.GetListAsync();

        return new ListResultDto<LanguageDto>(ObjectMapper.Map<List<Language>, List<LanguageDto>>(languages));
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.LanguageManagement.Languages.Create)]
    public async Task<LanguageDto> CreateAsync(CreateLanguageDto input)
    {
        var language = new Language(
            _guidGenerator.Create(),
            input.CultureName ?? string.Empty,
            input.UiCultureName ?? string.Empty,
            input.DisplayName ?? string.Empty,
            input.FlagIcon,
            input.IsEnabled
        );

        await _languageRepository.InsertAsync(language, autoSave: true);

        return ObjectMapper.Map<Language, LanguageDto>(language);
    }

    /// <inheritdoc />
    public async Task<LanguageDto> GetAsync(Guid id)
    {
        var language = await _languageRepository.GetAsync(id);

        return ObjectMapper.Map<Language, LanguageDto>(language);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.LanguageManagement.Languages.Edit)]
    public async Task<LanguageDto> UpdateAsync(Guid id, UpdateLanguageDto input)
    {
        var language = await _languageRepository.GetAsync(id);

        // Update properties
        language.DisplayName = input.DisplayName ?? language.DisplayName;
        language.FlagIcon = input.FlagIcon;
        language.IsEnabled = input.IsEnabled;

        await _languageRepository.UpdateAsync(language, autoSave: true);

        return ObjectMapper.Map<Language, LanguageDto>(language);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.LanguageManagement.Languages.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _languageRepository.DeleteAsync(id);
    }

    /// <inheritdoc />
    public Task<List<LanguageResourceDto>> GetResourcesAsync()
    {
        var resources = _abpLocalizationOptions.Resources
            .Select(r => new LanguageResourceDto
            {
                Name = r.Value.ResourceName
            })
            .ToList();

        return Task.FromResult(resources);
    }

    /// <inheritdoc />
    public async Task<List<CultureInfoDto>> GetCultureListAsync()
    {
        var cultures = _abpLocalizationOptions.Languages
            .Select(l => new CultureInfoDto
            {
                DisplayName = l.DisplayName,
                Name = l.CultureName
            })
            .ToList();
        var languages = await _languageRepository.GetListAsync();
        foreach (var language in languages)
        {
            if(cultures.Any(c => c.Name == language.CultureName)) continue;
            cultures.Add(new CultureInfoDto
            {
                DisplayName = language.DisplayName,
                Name = language.CultureName
            });
        }

        return cultures;
    }
}
