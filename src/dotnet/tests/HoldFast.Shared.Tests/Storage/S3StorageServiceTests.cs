using HoldFast.Storage;
using Xunit;

namespace HoldFast.Shared.Tests.Storage;

/// <summary>
/// Tests for S3StorageService configuration and StorageOptions.
/// Full IAmazonS3 integration is not mockable without Moq (interface is enormous in SDK v4).
/// These tests cover the configuration/options layer and defaults.
/// </summary>
public class S3StorageServiceTests
{
    // ── StorageOptions ────────────────────────────────────────────────

    [Fact]
    public void StorageOptions_Defaults()
    {
        var options = new StorageOptions();
        Assert.Equal("filesystem", options.Type);
        Assert.Equal("/tmp/holdfast-storage", options.FilesystemRoot);
        Assert.Null(options.S3BucketName);
        Assert.Null(options.S3Region);
    }

    [Fact]
    public void StorageOptions_SetS3Values()
    {
        var options = new StorageOptions
        {
            Type = "s3",
            S3BucketName = "my-bucket",
            S3Region = "us-west-2",
        };
        Assert.Equal("s3", options.Type);
        Assert.Equal("my-bucket", options.S3BucketName);
        Assert.Equal("us-west-2", options.S3Region);
    }

    [Fact]
    public void StorageOptions_SetFilesystemValues()
    {
        var options = new StorageOptions
        {
            Type = "filesystem",
            FilesystemRoot = "/data/holdfast",
        };
        Assert.Equal("filesystem", options.Type);
        Assert.Equal("/data/holdfast", options.FilesystemRoot);
    }

    [Fact]
    public void StorageOptions_EmptyBucketName()
    {
        var options = new StorageOptions { S3BucketName = "" };
        Assert.Equal("", options.S3BucketName);
    }

    [Fact]
    public void StorageOptions_AllFieldsSet()
    {
        var options = new StorageOptions
        {
            Type = "s3",
            FilesystemRoot = "/fallback",
            S3BucketName = "production-bucket",
            S3Region = "eu-central-1",
        };
        Assert.Equal("s3", options.Type);
        Assert.Equal("/fallback", options.FilesystemRoot);
        Assert.Equal("production-bucket", options.S3BucketName);
        Assert.Equal("eu-central-1", options.S3Region);
    }

    // ── IStorageService Contract ──────────────────────────────────────

    [Fact]
    public void IStorageService_DefinedMethods()
    {
        // Verify the interface has expected methods
        var methods = typeof(IStorageService).GetMethods();
        var names = methods.Select(m => m.Name).ToArray();

        Assert.Contains("UploadAsync", names);
        Assert.Contains("DownloadAsync", names);
        Assert.Contains("ExistsAsync", names);
        Assert.Contains("DeleteAsync", names);
        Assert.Contains("GetDownloadUrlAsync", names);
    }

    [Fact]
    public void S3StorageService_ImplementsIStorageService()
    {
        Assert.True(typeof(IStorageService).IsAssignableFrom(typeof(S3StorageService)));
    }

    [Fact]
    public void FilesystemStorageService_ImplementsIStorageService()
    {
        Assert.True(typeof(IStorageService).IsAssignableFrom(typeof(FilesystemStorageService)));
    }

    // TODO: Discuss whether we need cloud storage at all for a self-hosted platform.
    // HoldFast is on-premise only — filesystem storage may be sufficient.
    // S3 support adds AWS dependency. Consider if MinIO compatibility is enough,
    // or if we should simplify to filesystem-only with optional S3-compatible
    // object storage via a lighter interface.
}
