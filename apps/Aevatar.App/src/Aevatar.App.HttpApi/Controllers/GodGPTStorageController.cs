using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Application.Constants;
using Aevatar.App.Application.Contracts.BlobStorings;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Application.Services;
using Aevatar.App.Application.Services.User;
using Aevatar.App.Domain.Shared;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.GodGPT.Dtos;
using Aevatar.Options;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BlobStoring;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT file/image storage management.
/// Handles file upload, deletion, and upload permission checks.
/// </summary>
[RemoteService]
[ControllerName("GodGPTStorage")]
[Route("api")]
[Authorize]
public class GodGPTStorageController : AevatarController
{
    private readonly IGodGPTUserService _userService;
    private readonly ILogger<GodGPTStorageController> _logger;
    private readonly IBlobContainer _blobContainer;
    private readonly BlobStoringOptions _blobStoringOptions;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILocalizationService _localizationService;

    public GodGPTStorageController(
        IGodGPTUserService userService,
        ILogger<GodGPTStorageController> logger,
        IBlobContainer blobContainer,
        IOptionsSnapshot<BlobStoringOptions> blobStoringOptions,
        IThumbnailService thumbnailService,
        ILocalizationService localizationService)
    {
        _userService = userService;
        _logger = logger;
        _blobContainer = blobContainer;
        _blobStoringOptions = blobStoringOptions.Value;
        _thumbnailService = thumbnailService;
        _localizationService = localizationService;
    }

    /// <summary>
    /// Check if user can upload image (daily limit check)
    /// </summary>
    [HttpGet("godgpt/can-upload-image")]
    public async Task<CanUploadImageResponseDto> CanUploadImageAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var language = HttpContext.GetGodGPTLanguage();
        var response = await _userService.CanUploadImageAsync(currentUserId, language);

        var result = new CanUploadImageResponseDto
        {
            CanUpload = response.Success,
            Reason = response.Message
        };

        _logger.LogDebug($"[GodGPTStorageController][CanUploadImageAsync] userId: {currentUserId}, canUpload: {result.CanUpload}, duration: {stopwatch.ElapsedMilliseconds}ms");

        return result;
    }

    /// <summary>
    /// Upload a file (image)
    /// </summary>
    [HttpPost("godgpt/blob")]
    public async Task<string> SaveAsync([FromForm] SaveBlobInput input)
    {
        var language = HttpContext.GetGodGPTLanguage();
        if (input.File.Length > _blobStoringOptions.MaxSizeBytes)
        {
            var parameters = new System.Collections.Generic.Dictionary<string, string>
            {
                ["MaxSizeBytes"] = _blobStoringOptions.MaxSizeBytes.ToString()
            };
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.FileTooLarge, language, parameters);
            throw new UserFriendlyException(localizedMessage);
        }

        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        var response = await _userService.CanUploadImageAsync(currentUserId);
        if (!response.Success)
        {
            _logger.LogDebug("[GodGPTStorageController][SaveAsync] Daily upload limit reached");
            throw new UserFriendlyException("Daily upload limit reached. Upgrade to premium to continue.");
        }

        var originalFileName = input.File.FileName;
        var fileExtension = Path.GetExtension(originalFileName);
        var fileName = Guid.NewGuid().ToString() + fileExtension;

        // Read file content to memory to avoid stream reuse issues
        var readStopwatch = Stopwatch.StartNew();
        byte[] fileContent;
        using (var fileStream = input.File.OpenReadStream())
        {
            fileContent = new byte[input.File.Length];
            await fileStream.ReadAsync(fileContent, 0, fileContent.Length);
        }
        readStopwatch.Stop();
        _logger.LogDebug("[GodGPTStorageController][SaveAsync] File reading completed: Duration={ReadTime}ms, Size={FileSize} bytes",
            readStopwatch.ElapsedMilliseconds, input.File.Length);

        // Save original file using memory stream
        var saveStopwatch = Stopwatch.StartNew();
        using (var originalStream = new MemoryStream(fileContent))
        {
            await _blobContainer.SaveAsync(fileName, originalStream, true);
        }
        saveStopwatch.Stop();
        _logger.LogDebug("[GodGPTStorageController][SaveAsync] Original file save completed: FileName={FileName}, Duration={SaveTime}ms",
            fileName, saveStopwatch.ElapsedMilliseconds);

        // Generate thumbnails using separate memory stream
        _ = Task.Run(async () =>
        {
            using var thumbnailStream = new MemoryStream(fileContent);
            await _thumbnailService.GenerateThumbnailsAsync(thumbnailStream, fileName);
        });

        _logger.LogDebug("[GodGPTStorageController][SaveAsync] userId: {0}, duration: {1}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return fileName;
    }

    /// <summary>
    /// Delete a file by name
    /// </summary>
    [HttpDelete("godgpt/blob/{name}")]
    public async Task DeleteAsync(string name)
    {
        if (name.IsNullOrWhiteSpace())
        {
            return;
        }

        await _blobContainer.DeleteAsync(name);
    }
}
