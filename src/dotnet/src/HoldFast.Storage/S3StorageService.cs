using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Storage;

/// <summary>
/// S3-backed storage for production/cloud deployments.
/// </summary>
public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(IAmazonS3 s3, IOptions<StorageOptions> options, ILogger<S3StorageService> logger)
    {
        _s3 = s3;
        _bucketName = options.Value.S3BucketName ?? "holdfast-storage";
        _logger = logger;
    }

    private string GetKey(string bucket, string key) => $"{bucket}/{key}";

    public async Task UploadAsync(string bucket, string key, Stream data, string? contentType, CancellationToken ct)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = GetKey(bucket, key),
            InputStream = data,
            ContentType = contentType ?? "application/octet-stream",
        };

        await _s3.PutObjectAsync(request, ct);
        _logger.LogDebug("Uploaded to S3: {Bucket}/{Key}", bucket, key);
    }

    public async Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucketName, GetKey(bucket, key), ct);
            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            ms.Position = 0;
            return ms;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucketName, GetKey(bucket, key), ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken ct)
    {
        await _s3.DeleteObjectAsync(_bucketName, GetKey(bucket, key), ct);
    }

    public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = GetKey(bucket, key),
            Expires = DateTime.UtcNow.Add(expiry),
        };

        return Task.FromResult(_s3.GetPreSignedURL(request));
    }
}
