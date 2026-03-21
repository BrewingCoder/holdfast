using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Storage;

/// <summary>
/// Storage backend configuration. Type selects "filesystem" (default) or "s3".
/// FilesystemRoot is the base directory for local storage. S3 settings apply when Type="s3".
/// </summary>
public class StorageOptions
{
    public string Type { get; set; } = "filesystem";
    public string FilesystemRoot { get; set; } = "/tmp/holdfast-storage";
    public string? S3BucketName { get; set; }
    public string? S3Region { get; set; }
}

/// <summary>
/// Filesystem-backed storage for self-hosted deployments.
/// Session replay chunks, sourcemaps, and assets are stored as files.
/// </summary>
public class FilesystemStorageService : IStorageService
{
    private readonly string _root;
    private readonly ILogger<FilesystemStorageService> _logger;

    public FilesystemStorageService(IOptions<StorageOptions> options, ILogger<FilesystemStorageService> logger)
    {
        _root = options.Value.FilesystemRoot;
        _logger = logger;
        Directory.CreateDirectory(_root);
    }

    private string GetPath(string bucket, string key) =>
        Path.Combine(_root, bucket, key);

    public async Task UploadAsync(string bucket, string key, Stream data, string? contentType, CancellationToken ct)
    {
        var path = GetPath(bucket, key);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        await using var file = File.Create(path);
        await data.CopyToAsync(file, ct);
        _logger.LogDebug("Stored {Bucket}/{Key} ({Bytes} bytes)", bucket, key, file.Length);
    }

    public async Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct)
    {
        var path = GetPath(bucket, key);
        if (!File.Exists(path)) return null;

        var ms = new MemoryStream();
        await using var file = File.OpenRead(path);
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct)
    {
        return Task.FromResult(File.Exists(GetPath(bucket, key)));
    }

    public Task DeleteAsync(string bucket, string key, CancellationToken ct)
    {
        var path = GetPath(bucket, key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct)
    {
        // For filesystem, return the file path directly
        return Task.FromResult(GetPath(bucket, key));
    }
}
