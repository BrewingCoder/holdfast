using HoldFast.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Tests.Storage;

public class FilesystemStorageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly IStorageService _storage;

    public FilesystemStorageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "holdfast-test-" + Guid.NewGuid().ToString("N"));
        _storage = new FilesystemStorageService(
            Options.Create(new StorageOptions { FilesystemRoot = _root }),
            NullLogger<FilesystemStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static MemoryStream StreamFromString(string content) =>
        new(System.Text.Encoding.UTF8.GetBytes(content));

    private static async Task<string> ReadStreamAsString(Stream stream) =>
        System.Text.Encoding.UTF8.GetString(((MemoryStream)stream).ToArray());

    // ── Upload ──────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_CreatesFile()
    {
        await _storage.UploadAsync("bucket", "key.txt", StreamFromString("hello"), null);

        var path = Path.Combine(_root, "bucket", "key.txt");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Upload_FileHasCorrectContent()
    {
        await _storage.UploadAsync("bucket", "key.txt", StreamFromString("content123"), null);

        var content = await File.ReadAllTextAsync(Path.Combine(_root, "bucket", "key.txt"));
        Assert.Equal("content123", content);
    }

    [Fact]
    public async Task Upload_NestedKey_CreatesDirectories()
    {
        await _storage.UploadAsync("bucket", "a/b/c/deep.txt", StreamFromString("deep"), null);

        var path = Path.Combine(_root, "bucket", "a", "b", "c", "deep.txt");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Upload_Overwrite_ReplacesExistingFile()
    {
        await _storage.UploadAsync("bucket", "key.txt", StreamFromString("original"), null);
        await _storage.UploadAsync("bucket", "key.txt", StreamFromString("replaced"), null);

        var content = await File.ReadAllTextAsync(Path.Combine(_root, "bucket", "key.txt"));
        Assert.Equal("replaced", content);
    }

    [Fact]
    public async Task Upload_EmptyStream_CreatesEmptyFile()
    {
        await _storage.UploadAsync("bucket", "empty.txt", new MemoryStream(), null);

        var path = Path.Combine(_root, "bucket", "empty.txt");
        Assert.True(File.Exists(path));
        Assert.Equal(0, new FileInfo(path).Length);
    }

    [Fact]
    public async Task Upload_LargeFile_Works()
    {
        var data = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(data);
        await _storage.UploadAsync("bucket", "large.bin", new MemoryStream(data), "application/octet-stream");

        var stored = await File.ReadAllBytesAsync(Path.Combine(_root, "bucket", "large.bin"));
        Assert.Equal(data, stored);
    }

    [Fact]
    public async Task Upload_BinaryContent_Preserved()
    {
        var data = new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x80, 0x7F };
        await _storage.UploadAsync("bucket", "binary.bin", new MemoryStream(data), null);

        var stored = await File.ReadAllBytesAsync(Path.Combine(_root, "bucket", "binary.bin"));
        Assert.Equal(data, stored);
    }

    // ── Download ────────────────────────────────────────────────────

    [Fact]
    public async Task Download_ExistingFile_ReturnsContent()
    {
        await _storage.UploadAsync("bucket", "test.txt", StreamFromString("hi there"), null);

        var stream = await _storage.DownloadAsync("bucket", "test.txt");
        Assert.NotNull(stream);
        Assert.Equal("hi there", await ReadStreamAsString(stream!));
    }

    [Fact]
    public async Task Download_NonExistentFile_ReturnsNull()
    {
        var stream = await _storage.DownloadAsync("bucket", "missing.txt");
        Assert.Null(stream);
    }

    [Fact]
    public async Task Download_NonExistentBucket_ReturnsNull()
    {
        var stream = await _storage.DownloadAsync("no-such-bucket", "key.txt");
        Assert.Null(stream);
    }

    [Fact]
    public async Task Download_StreamPositionIsZero()
    {
        await _storage.UploadAsync("bucket", "pos.txt", StreamFromString("data"), null);
        var stream = await _storage.DownloadAsync("bucket", "pos.txt");
        Assert.Equal(0, stream!.Position);
    }

    // ── Exists ──────────────────────────────────────────────────────

    [Fact]
    public async Task Exists_AfterUpload_ReturnsTrue()
    {
        await _storage.UploadAsync("bucket", "exists.txt", StreamFromString("x"), null);
        Assert.True(await _storage.ExistsAsync("bucket", "exists.txt"));
    }

    [Fact]
    public async Task Exists_NonExistent_ReturnsFalse()
    {
        Assert.False(await _storage.ExistsAsync("bucket", "ghost.txt"));
    }

    [Fact]
    public async Task Exists_AfterDelete_ReturnsFalse()
    {
        await _storage.UploadAsync("bucket", "temp.txt", StreamFromString("tmp"), null);
        await _storage.DeleteAsync("bucket", "temp.txt");
        Assert.False(await _storage.ExistsAsync("bucket", "temp.txt"));
    }

    // ── Delete ──────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingFile_RemovesIt()
    {
        await _storage.UploadAsync("bucket", "del.txt", StreamFromString("bye"), null);
        await _storage.DeleteAsync("bucket", "del.txt");

        var path = Path.Combine(_root, "bucket", "del.txt");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Delete_NonExistentFile_DoesNotThrow()
    {
        // Should silently succeed
        await _storage.DeleteAsync("bucket", "nonexistent.txt");
    }

    [Fact]
    public async Task Delete_Idempotent()
    {
        await _storage.UploadAsync("bucket", "idem.txt", StreamFromString("x"), null);
        await _storage.DeleteAsync("bucket", "idem.txt");
        await _storage.DeleteAsync("bucket", "idem.txt"); // second delete should not throw
    }

    // ── GetDownloadUrl ──────────────────────────────────────────────

    [Fact]
    public async Task GetDownloadUrl_ReturnsFilePath()
    {
        var url = await _storage.GetDownloadUrlAsync("bucket", "key.txt", TimeSpan.FromMinutes(5));
        Assert.Equal(Path.Combine(_root, "bucket", "key.txt"), url);
    }

    // ── Edge cases ──────────────────────────────────────────────────

    [Fact]
    public async Task Upload_MultipleBuckets_Isolated()
    {
        await _storage.UploadAsync("bucket-a", "key.txt", StreamFromString("aaa"), null);
        await _storage.UploadAsync("bucket-b", "key.txt", StreamFromString("bbb"), null);

        var a = await _storage.DownloadAsync("bucket-a", "key.txt");
        var b = await _storage.DownloadAsync("bucket-b", "key.txt");

        Assert.Equal("aaa", await ReadStreamAsString(a!));
        Assert.Equal("bbb", await ReadStreamAsString(b!));
    }

    [Fact]
    public async Task Upload_SpecialCharsInKey()
    {
        // Filesystem-safe special chars
        await _storage.UploadAsync("bucket", "file with spaces.txt", StreamFromString("s"), null);
        Assert.True(await _storage.ExistsAsync("bucket", "file with spaces.txt"));
    }

    [Fact]
    public async Task RoundTrip_UploadDownloadDeleteVerify()
    {
        await _storage.UploadAsync("test", "round.bin", StreamFromString("roundtrip"), "text/plain");
        Assert.True(await _storage.ExistsAsync("test", "round.bin"));

        var downloaded = await _storage.DownloadAsync("test", "round.bin");
        Assert.Equal("roundtrip", await ReadStreamAsString(downloaded!));

        await _storage.DeleteAsync("test", "round.bin");
        Assert.False(await _storage.ExistsAsync("test", "round.bin"));
        Assert.Null(await _storage.DownloadAsync("test", "round.bin"));
    }

    [Fact]
    public void Constructor_CreatesRootDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "holdfast-ctor-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = new FilesystemStorageService(
                Options.Create(new StorageOptions { FilesystemRoot = root }),
                NullLogger<FilesystemStorageService>.Instance);
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
