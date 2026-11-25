using System.IO;
using System.Net;
using System.Net.Http;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class ImageServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-img-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task ResolveBackdropAsync_DownloadsWithHeaderAndCachesResult()
    {
        Directory.CreateDirectory(_tempDir);
        var pngBytes = CacheServiceTests.CreateValidPngBytes(256);
        var imageDownloadCalls = 0;

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.0"}""");
            }

            if (path.Contains("/Users/AuthenticateByName", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"User":{"Id":"u-1","Name":"ali"},"AccessToken":"secret-tok","ServerId":"srv-1"}""");
            }

            if (path.Contains("/Images/Backdrop/", StringComparison.Ordinal))
            {
                imageDownloadCalls++;
                Assert.DoesNotContain("secret-tok", path, StringComparison.Ordinal);
                Assert.Equal("MediaBrowser", req.Headers.Authorization?.Scheme);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pngBytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var (imageService, _, _) = await CreateAuthenticatedImageServiceAsync(handler);
        var item = new MediaItem
        {
            Id = "movie-1",
            Name = "Interstellar",
            Type = "Movie",
            BackdropImageTags = ["tag0"]
        };

        var first = await imageService.ResolveBackdropAsync(item);
        var second = await imageService.ResolveBackdropAsync(item);

        Assert.Equal(BackdropResolveStatus.Success, first.Status);
        Assert.True(first.HasImage);
        Assert.Equal(1, imageDownloadCalls);
        Assert.Equal(first.LocalFilePath, second.LocalFilePath);
    }

    [Fact]
    public async Task ResolveBackdropAsync_RejectsCorruptBytesWithoutCrashing()
    {
        Directory.CreateDirectory(_tempDir);

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.0"}""");
            }

            if (path.Contains("/Users/AuthenticateByName", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"User":{"Id":"u-1","Name":"ali"},"AccessToken":"tok","ServerId":"srv-1"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("not-an-image"u8.ToArray())
            };
        });

        var (imageService, _, _) = await CreateAuthenticatedImageServiceAsync(handler);
        var item = new MediaItem
        {
            Id = "movie-bad",
            Name = "Corrupt",
            BackdropImageTags = ["tag0"]
        };

        var result = await imageService.ResolveBackdropAsync(item);

        Assert.Equal(BackdropResolveStatus.FailedCorruptOrIo, result.Status);
        Assert.False(result.HasImage);
    }

    [Fact]
    public async Task ResolveBackdropAsync_UsesFallbackOrSkipsWhenNoBackdrop()
    {
        Directory.CreateDirectory(_tempDir);
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.0"}""");
            }

            return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"User":{"Id":"u-1","Name":"ali"},"AccessToken":"tok","ServerId":"srv-1"}""");
        });

        var (imageService, settings, _) = await CreateAuthenticatedImageServiceAsync(handler);
        var itemWithoutBackdrop = new MediaItem
        {
            Id = "series-1",
            Name = "No Backdrop Show",
            BackdropImageTags = []
        };

        var skipped = await imageService.ResolveBackdropAsync(itemWithoutBackdrop);
        Assert.Equal(BackdropResolveStatus.SkippedNoBackdrop, skipped.Status);

        var fallbackFile = Path.Combine(_tempDir, "fallback.png");
        await File.WriteAllBytesAsync(fallbackFile, CacheServiceTests.CreateValidPngBytes(128));
        settings.Current.FallbackImagePath = fallbackFile;

        var usedFallback = await imageService.ResolveBackdropAsync(itemWithoutBackdrop);
        Assert.Equal(BackdropResolveStatus.UsedFallback, usedFallback.Status);
        Assert.Equal(fallbackFile, usedFallback.LocalFilePath);
    }

    [Fact]
    public async Task ResolveBackdropAsync_FallsBackToOfflineCacheWhenServerUnreachable()
    {
        Directory.CreateDirectory(_tempDir);
        var failNetwork = false;
        var pngBytes = CacheServiceTests.CreateValidPngBytes(128);

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.0"}""");
            }

            if (path.Contains("/Users/AuthenticateByName", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"User":{"Id":"u-1","Name":"ali"},"AccessToken":"tok","ServerId":"srv-1"}""");
            }

            if (failNetwork)
            {
                throw new HttpRequestException("Offline");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(pngBytes)
            };
        });

        var (imageService, _, cache) = await CreateAuthenticatedImageServiceAsync(handler);
        await cache.SaveToCacheAsync("movie-offline", 0, "oldtag", pngBytes, ".png");
        failNetwork = true;

        var item = new MediaItem
        {
            Id = "movie-offline",
            Name = "Cached Movie",
            BackdropImageTags = ["newtag"]
        };

        var result = await imageService.ResolveBackdropAsync(item);
        Assert.Equal(BackdropResolveStatus.UsedCacheOffline, result.Status);
        Assert.True(result.HasImage);
    }

    private async Task<(ImageService ImageService, SettingsService Settings, CacheService Cache)> CreateAuthenticatedImageServiceAsync(
        StubHttpMessageHandler handler)
    {
        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, Path.Combine(_tempDir, "settings.json"));
        settings.Load();

        var client = new JellyfinClient(new HttpClient(handler), logger);
        var auth = new AuthenticationService(client, settings, new InMemorySecureStorageService(), logger);
        await auth.LoginAsync("http://192.168.1.10:8096", "ali", "pass");

        var cache = new CacheService(logger, Path.Combine(_tempDir, "cache"));
        var imageService = new ImageService(client, auth, cache, settings, logger);
        return (imageService, settings, cache);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
