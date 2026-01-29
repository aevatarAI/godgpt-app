using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Aevatar.App.Permissions;
using Aevatar.App.Terms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Users;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// Application service for Terms of Service management.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize]
public class TermsOfServiceAppService : AppAppService, ITermsOfServiceAppService
{
    private readonly IRepository<UserTermsConsent, Guid> _userConsentRepository;
    private readonly IRepository<TermsOfServiceVersion, Guid> _termsVersionRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TermsOfServiceAppService> _logger;

    public TermsOfServiceAppService(
        IRepository<UserTermsConsent, Guid> userConsentRepository,
        IRepository<TermsOfServiceVersion, Guid> termsVersionRepository,
        IGuidGenerator guidGenerator,
        IHttpContextAccessor httpContextAccessor,
        ILogger<TermsOfServiceAppService> logger)
    {
        _userConsentRepository = userConsentRepository;
        _termsVersionRepository = termsVersionRepository;
        _guidGenerator = guidGenerator;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConsentStatusDto> GetConsentStatusAsync()
    {
        var userId = CurrentUser.GetId();
        
        // Get user's latest consent
        var latestConsent = await GetLatestConsentAsync(userId);
        
        // Get current active version
        var currentVersion = await GetCurrentActiveVersionAsync();
        
        if (currentVersion == null)
        {
            // No ToS version configured yet
            return new ConsentStatusDto
            {
                CurrentVersion = "0.0.0",
                HasConsented = false,
                ConsentedAt = null,
                TermsUrl = string.Empty
            };
        }

        // Check if user has consented to current version
        var hasConsentedCurrentVersion = latestConsent != null && 
            CompareVersions(latestConsent.Version, currentVersion.Version) >= 0;

        return new ConsentStatusDto
        {
            CurrentVersion = currentVersion.Version,
            HasConsented = hasConsentedCurrentVersion,
            ConsentedAt = hasConsentedCurrentVersion ? latestConsent?.ConsentedAt : null,
            TermsUrl = currentVersion.ContentUrl
        };
    }

    /// <inheritdoc />
    public async Task<SubmitConsentResponseDto> SubmitConsentAsync(SubmitConsentRequestDto input)
    {
        var userId = CurrentUser.GetId();
        var now = Clock.Now;
        
        // Validate version exists
        var version = await GetVersionByNumberAsync(input.Version);
        if (version == null)
        {
            throw new UserFriendlyException($"Terms of Service version '{input.Version}' not found.");
        }

        // Validate content hash matches
        if (!string.Equals(version.ContentHash, input.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Content hash mismatch for user {UserId}, version {Version}. Expected: {Expected}, Got: {Got}",
                userId, input.Version, version.ContentHash, input.ContentHash);
            throw new UserFriendlyException("Content verification failed. Please refresh and try again.");
        }

        // Get IP address from request
        var ipAddress = GetClientIpAddress();

        // Create consent record (always append, never update)
        var consent = new UserTermsConsent(
            _guidGenerator.Create(),
            userId,
            input.Version,
            input.ContentHash,
            now,
            input.Platform,
            ipAddress,
            input.OsVersion,
            input.AppVersion
        );

        await _userConsentRepository.InsertAsync(consent, autoSave: true);

        _logger.LogInformation(
            "User {UserId} consented to ToS version {Version} from {Platform} {OsVersion}",
            userId, input.Version, input.Platform, input.OsVersion);

        return new SubmitConsentResponseDto
        {
            Success = true,
            ConsentId = consent.Id,
            ConsentedAt = consent.ConsentedAt,
            Version = consent.Version
        };
    }

    #region Admin APIs

    /// <inheritdoc />
    [Authorize(AppPermissions.TermsManagement.Versions.Default)]
    public async Task<PagedResultDto<TermsVersionDto>> GetVersionsAsync(GetTermsVersionsInput input)
    {
        var queryable = await _termsVersionRepository.GetQueryableAsync();

        // Apply filters
        queryable = queryable
            .WhereIf(!string.IsNullOrWhiteSpace(input.VersionFilter),
                x => x.Version.Contains(input.VersionFilter!))
            .WhereIf(input.IsActiveFilter.HasValue,
                x => x.IsActive == input.IsActiveFilter!.Value)
            .WhereIf(!string.IsNullOrWhiteSpace(input.LanguageFilter),
                x => x.Language == input.LanguageFilter);

        // Get total count
        var totalCount = await AsyncExecuter.CountAsync(queryable);

        // Apply sorting and pagination
        var sorting = string.IsNullOrWhiteSpace(input.Sorting) ? "CreationTime desc" : input.Sorting;
        var versions = await AsyncExecuter.ToListAsync(
            queryable.OrderBy(sorting).PageBy(input)
        );

        var dtos = ObjectMapper.Map<List<TermsOfServiceVersion>, List<TermsVersionDto>>(versions);
        return new PagedResultDto<TermsVersionDto>(totalCount, dtos);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.TermsManagement.Versions.Default)]
    public async Task<TermsVersionDto> GetVersionAsync(Guid id)
    {
        var version = await _termsVersionRepository.GetAsync(id);
        return ObjectMapper.Map<TermsOfServiceVersion, TermsVersionDto>(version);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.TermsManagement.Versions.Create)]
    public async Task<TermsVersionDto> CreateVersionAsync(CreateTermsVersionDto input)
    {
        // Check if version already exists
        var existing = await GetVersionByNumberAsync(input.Version);
        if (existing != null)
        {
            throw new UserFriendlyException($"Version '{input.Version}' already exists.");
        }

        var version = new TermsOfServiceVersion(
            _guidGenerator.Create(),
            input.Version,
            input.ContentHash,
            input.ContentUrl,
            input.EffectiveDate,
            input.IsActive,
            input.StorageVersionId,
            input.ContentBackup,
            input.Language
        );

        // If this version is being set as active, deactivate others
        if (input.IsActive)
        {
            await DeactivateAllVersionsAsync();
            version.IsActive = true;
        }

        await _termsVersionRepository.InsertAsync(version, autoSave: true);

        _logger.LogInformation("Created ToS version {Version} by user {UserId}", 
            input.Version, CurrentUser.Id);

        return ObjectMapper.Map<TermsOfServiceVersion, TermsVersionDto>(version);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.TermsManagement.Versions.Edit)]
    public async Task<TermsVersionDto> UpdateVersionAsync(Guid id, UpdateTermsVersionDto input)
    {
        var version = await _termsVersionRepository.GetAsync(id);

        // Update only provided fields
        if (!string.IsNullOrWhiteSpace(input.ContentHash))
            version.ContentHash = input.ContentHash;
        
        if (!string.IsNullOrWhiteSpace(input.ContentUrl))
            version.ContentUrl = input.ContentUrl;
        
        if (!string.IsNullOrWhiteSpace(input.StorageVersionId))
            version.StorageVersionId = input.StorageVersionId;
        
        if (input.EffectiveDate.HasValue)
            version.EffectiveDate = input.EffectiveDate.Value;
        
        if (input.Language != null)
            version.Language = input.Language;
        
        if (input.ContentBackup != null)
            version.ContentBackup = input.ContentBackup;

        await _termsVersionRepository.UpdateAsync(version, autoSave: true);

        _logger.LogInformation("Updated ToS version {Version} by user {UserId}", 
            version.Version, CurrentUser.Id);

        return ObjectMapper.Map<TermsOfServiceVersion, TermsVersionDto>(version);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.TermsManagement.Versions.Delete)]
    public async Task DeleteVersionAsync(Guid id)
    {
        var version = await _termsVersionRepository.GetAsync(id);
        
        if (version.IsActive)
        {
            throw new UserFriendlyException("Cannot delete the active version. Please activate another version first.");
        }

        // Check if any users have consented to this version
        var consentCount = await GetConsentCountByVersionAsync(version.Version);
        if (consentCount > 0)
        {
            _logger.LogWarning(
                "Deleting ToS version {Version} with {ConsentCount} user consents by user {UserId}",
                version.Version, consentCount, CurrentUser.Id);
        }

        await _termsVersionRepository.DeleteAsync(id);

        _logger.LogInformation("Deleted ToS version {Version} by user {UserId}", 
            version.Version, CurrentUser.Id);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.TermsManagement.Versions.Edit)]
    public async Task ActivateVersionAsync(Guid id)
    {
        var version = await _termsVersionRepository.GetAsync(id);

        // Deactivate all other versions
        await DeactivateAllVersionsAsync();

        // Activate this version
        version.IsActive = true;
        await _termsVersionRepository.UpdateAsync(version, autoSave: true);

        _logger.LogInformation("Activated ToS version {Version} by user {UserId}", 
            version.Version, CurrentUser.Id);
    }

    /// <inheritdoc />
    [Authorize(AppPermissions.TermsManagement.Versions.Default)]
    public async Task<long> GetVersionConsentCountAsync(string version)
    {
        return await GetConsentCountByVersionAsync(version);
    }

    #endregion

    #region Private Methods

    private async Task DeactivateAllVersionsAsync()
    {
        var queryable = await _termsVersionRepository.GetQueryableAsync();
        var activeVersions = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.IsActive));

        foreach (var v in activeVersions)
        {
            v.IsActive = false;
            await _termsVersionRepository.UpdateAsync(v);
        }
    }

    private async Task<TermsOfServiceVersion?> GetCurrentActiveVersionAsync()
    {
        var queryable = await _termsVersionRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x => x.IsActive).OrderByDescending(x => x.EffectiveDate));
    }

    private async Task<TermsOfServiceVersion?> GetVersionByNumberAsync(string version)
    {
        var queryable = await _termsVersionRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x => x.Version == version));
    }

    private string? GetClientIpAddress()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return null;

        // Check for forwarded IP (behind load balancer/proxy)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').FirstOrDefault()?.Trim();
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    private async Task<UserTermsConsent?> GetLatestConsentAsync(Guid userId)
    {
        var queryable = await _userConsentRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x => x.UserId == userId).OrderByDescending(x => x.ConsentedAt));
    }

    private async Task<long> GetConsentCountByVersionAsync(string version)
    {
        var queryable = await _userConsentRepository.GetQueryableAsync();
        return await AsyncExecuter.LongCountAsync(queryable.Where(x => x.Version == version));
    }

    /// <summary>
    /// Compares two semantic version strings.
    /// Returns positive if v1 > v2, negative if v1 < v2, 0 if equal.
    /// </summary>
    private static int CompareVersions(string v1, string v2)
    {
        var parts1 = v1.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var parts2 = v2.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        var maxLength = Math.Max(parts1.Length, parts2.Length);
        
        for (var i = 0; i < maxLength; i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : 0;
            var p2 = i < parts2.Length ? parts2[i] : 0;
            
            if (p1 != p2) return p1.CompareTo(p2);
        }

        return 0;
    }

    #endregion
}
