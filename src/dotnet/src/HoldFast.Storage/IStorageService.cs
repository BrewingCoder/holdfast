namespace HoldFast.Storage;

/// <summary>
/// Abstraction over session replay and asset storage.
/// Supports filesystem (self-hosted default) and S3.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Upload data to storage.
    /// </summary>
    Task UploadAsync(string bucket, string key, Stream data, string? contentType = null, CancellationToken ct = default);

    /// <summary>
    /// Download data from storage.
    /// </summary>
    Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>
    /// Check if a key exists.
    /// </summary>
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>
    /// Delete a key.
    /// </summary>
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>
    /// Get a presigned download URL (for S3) or a direct file path (for filesystem).
    /// </summary>
    Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct = default);
}
