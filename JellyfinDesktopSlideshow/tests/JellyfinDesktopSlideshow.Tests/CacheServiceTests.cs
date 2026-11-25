using System.IO;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class CacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-cache-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndRetrieve_WorksWithValidImageBytes()
    {
        var cache = new CacheService(new NullLoggingService(), _tempDir);
        var png = CreateValidPngBytes(128);

        var savedPath = await cache.SaveToCacheAsync("item-1", 0, "tagA", png, ".png");
        Assert.NotNull(savedPath);
        Assert.True(File.Exists(savedPath));

        var hit = cache.TryGetCachedFile("item-1", 0, "tagA");
        Assert.Equal(savedPath, hit);

        var stats = cache.GetStats();
        Assert.Equal(1, stats.FileCount);
        Assert.Equal(png.Length, stats.TotalBytes);
    }

    [Fact]
    public async Task PruneCache_DeletesExpiredFiles()
    {
        var cache = new CacheService(new NullLoggingService(), _tempDir);
        var png = CreateValidPngBytes(128);

        var oldPath = await cache.SaveToCacheAsync("old-item", 0, "t1", png, ".png");
        var freshPath = await cache.SaveToCacheAsync("new-item", 0, "t2", png, ".png");
        Assert.NotNull(oldPath);
        Assert.NotNull(freshPath);

        File.SetLastWriteTimeUtc(oldPath!, DateTime.UtcNow.AddDays(-20));

        cache.PruneCache(maxCacheSizeMb: 512, maxCacheAgeDays: 7);

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(freshPath));
    }

    [Fact]
    public async Task ClearCache_RemovesAllCachedFiles()
    {
        var cache = new CacheService(new NullLoggingService(), _tempDir);
        await cache.SaveToCacheAsync("item-1", 0, "t1", CreateValidPngBytes(128), ".png");

        cache.ClearCache();

        var stats = cache.GetStats();
        Assert.Equal(0, stats.FileCount);
        Assert.Equal(0, stats.TotalBytes);
    }

    public static byte[] CreateValidPngBytes(int size)
    {
        var bytes = new byte[Math.Max(64, size)];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;
        return bytes;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
