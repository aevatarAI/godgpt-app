using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BlobStoring;

namespace Aevatar.App.Application.Services;

/// <summary>
/// AWS S3 configuration options
/// </summary>
public class AwsS3Options
{
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string Region { get; set; } = "ap-northeast-1";
    public string ContainerName { get; set; } = string.Empty;
}

/// <summary>
/// Simple AWS S3 implementation of IBlobContainer.
/// Based on ABP's AwsBlobProvider implementation.
/// Key format: "host/{BlobName}" (matching ABP's DefaultAwsBlobNameCalculator without tenant)
/// </summary>
public class AwsS3BlobContainer : IBlobContainer
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<AwsS3BlobContainer> _logger;
    
    public AwsS3BlobContainer(
        IOptions<AwsS3Options> options,
        ILogger<AwsS3BlobContainer> logger)
    {
        _logger = logger;
        var config = options.Value;
        _bucketName = config.ContainerName;
        
        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(config.Region)
        };
        
        _s3Client = new AmazonS3Client(
            config.AccessKeyId,
            config.SecretAccessKey,
            s3Config);
        
        _logger.LogInformation("[AwsS3BlobContainer] Initialized: bucket={Bucket}, region={Region}",
            _bucketName, config.Region);
    }

    /// <summary>
    /// Calculate the S3 key using ABP's format: "host/{blobName}"
    /// </summary>
    private string CalculateKey(string blobName) => $"host/{blobName}";

    public async Task SaveAsync(string name, Stream stream, bool overrideExisting = false, 
        CancellationToken cancellationToken = default)
    {
        var key = CalculateKey(name);
        _logger.LogDebug("[AwsS3BlobContainer] Saving: {Key}", key);
        
        // ABP style: directly pass stream to PutObjectRequest
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream
        }, cancellationToken);
        
        _logger.LogInformation("[AwsS3BlobContainer] Saved to: {Key}", key);
    }

    public async Task<byte[]> GetAllBytesAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = CalculateKey(name);
        _logger.LogDebug("[AwsS3BlobContainer] Downloading: {Key}", key);
        
        var response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        }, cancellationToken);
        
        // ABP style: copy ResponseStream to MemoryStream
        using var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
        
        var bytes = memoryStream.ToArray();
        _logger.LogDebug("[AwsS3BlobContainer] Downloaded {Size} bytes from: {Key}", 
            bytes.Length, key);
        
        return bytes;
    }

    public async Task<byte[]?> GetAllBytesOrNullAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetAllBytesAsync(name, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("[AwsS3BlobContainer] Key not found: {Key}", CalculateKey(name));
            return null;
        }
    }

    public async Task<Stream> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var bytes = await GetAllBytesAsync(name, cancellationToken);
        return new MemoryStream(bytes);
    }

    public async Task<Stream?> GetOrNullAsync(string name, CancellationToken cancellationToken = default)
    {
        var bytes = await GetAllBytesOrNullAsync(name, cancellationToken);
        return bytes != null ? new MemoryStream(bytes) : null;
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = CalculateKey(name);
        try
        {
            await _s3Client.GetObjectMetadataAsync(_bucketName, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = CalculateKey(name);
        try
        {
            if (!await ExistsAsync(name, cancellationToken))
            {
                return false;
            }
            
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            }, cancellationToken);
            
            _logger.LogDebug("[AwsS3BlobContainer] Deleted: {Key}", key);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
