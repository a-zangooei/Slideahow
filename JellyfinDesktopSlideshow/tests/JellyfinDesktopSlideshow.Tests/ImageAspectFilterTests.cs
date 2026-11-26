using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class ImageAspectFilterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-aspect-test-{Guid.NewGuid():N}");

    [Fact]
    public void TryReadImageDimensions_ParsesPngJpegWebpAndBmpHeaders()
    {
        var png = CreatePngWithDimensions(1920, 1080);
        Assert.True(ImageFormatValidator.TryReadImageDimensions(png, out var pngW, out var pngH));
        Assert.Equal(1920, pngW);
        Assert.Equal(1080, pngH);

        var jpg = CreateJpegWithDimensions(2560, 1080);
        Assert.True(ImageFormatValidator.TryReadImageDimensions(jpg, out var jpgW, out var jpgH));
        Assert.Equal(2560, jpgW);
        Assert.Equal(1080, jpgH);

        var webpVp8L = CreateWebpVp8LWithDimensions(1000, 185);
        Assert.True(ImageFormatValidator.TryReadImageDimensions(webpVp8L, out var vp8LW, out var vp8LH));
        Assert.Equal(1000, vp8LW);
        Assert.Equal(185, vp8LH);

        var webpVp8X = CreateWebpVp8XWithDimensions(3840, 2160);
        Assert.True(ImageFormatValidator.TryReadImageDimensions(webpVp8X, out var vp8XW, out var vp8XH));
        Assert.Equal(3840, vp8XW);
        Assert.Equal(2160, vp8XH);

        var bmpTopDown = CreateBmpWithDimensions(1600, -900);
        Assert.True(ImageFormatValidator.TryReadImageDimensions(bmpTopDown, out var bmpW, out var bmpH));
        Assert.Equal(1600, bmpW);
        Assert.Equal(900, bmpH);
    }

    [Fact]
    public void TryReadImageDimensions_ReturnsFalseGracefullyForStubWithoutIhdr()
    {
        var stubPng = CacheServiceTests.CreateValidPngBytes(128);
        Assert.False(ImageFormatValidator.TryReadImageDimensions(stubPng, out _, out _));

        var settings = new AppSettings { FilterNonWallpaperAspectRatios = true };
        Assert.True(ImageAspectFilter.IsAcceptableBytes(stubPng, settings, out var reason));
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(1000, 185, false)]  // Jellyfin banner (~5.41:1)
    [InlineData(758, 140, false)]   // Wide banner (~5.41:1)
    [InlineData(3200, 800, false)]  // Very wide strip (4.00:1)
    [InlineData(800, 800, false)]   // Square cover (1.00:1)
    [InlineData(600, 900, false)]   // Vertical poster (0.67:1)
    [InlineData(1920, 1080, true)]  // Standard 16:9 (1.78:1)
    [InlineData(1920, 1200, true)]  // 16:10 (1.60:1)
    [InlineData(2560, 1080, true)]  // 21:9 CinemaScope (2.37:1)
    [InlineData(1600, 1200, true)]  // 4:3 (1.33:1)
    public void StandardPreset_FiltersBannersSquaresAndVerticalsWhileKeepingWallpapers(
        int width,
        int height,
        bool expectedAccepted)
    {
        var settings = new AppSettings
        {
            FilterNonWallpaperAspectRatios = true,
            BackdropAspectFilterPreset = BackdropAspectFilterPreset.Standard
        };

        var png = CreatePngWithDimensions(width, height);
        var accepted = ImageAspectFilter.IsAcceptableBytes(png, settings, out var reason);

        Assert.Equal(expectedAccepted, accepted);
        if (expectedAccepted)
        {
            Assert.Null(reason);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }

    [Fact]
    public void PresetsAndToggle_RespectStrictRelaxedCustomAndDisabledModes()
    {
        // 4:3 (1.33) is rejected under StrictWidescreen (min 1.45), accepted under Standard (min 1.25)
        var fourByThree = CreatePngWithDimensions(1600, 1200);
        var strict = new AppSettings
        {
            FilterNonWallpaperAspectRatios = true,
            BackdropAspectFilterPreset = BackdropAspectFilterPreset.StrictWidescreen
        };
        Assert.False(ImageAspectFilter.IsAcceptableBytes(fourByThree, strict, out _));

        // 3.00:1 image is rejected under Standard (max 2.80), accepted under RelaxedBannerOnly (max 3.20)
        var semiWide = CreatePngWithDimensions(3000, 1000);
        var standard = new AppSettings
        {
            FilterNonWallpaperAspectRatios = true,
            BackdropAspectFilterPreset = BackdropAspectFilterPreset.Standard
        };
        var relaxed = new AppSettings
        {
            FilterNonWallpaperAspectRatios = true,
            BackdropAspectFilterPreset = BackdropAspectFilterPreset.RelaxedBannerOnly
        };
        Assert.False(ImageAspectFilter.IsAcceptableBytes(semiWide, standard, out _));
        Assert.True(ImageAspectFilter.IsAcceptableBytes(semiWide, relaxed, out _));

        // Custom bounds
        var custom = new AppSettings
        {
            FilterNonWallpaperAspectRatios = true,
            BackdropAspectFilterPreset = BackdropAspectFilterPreset.Custom,
            MinBackdropAspectRatio = 1.70,
            MaxBackdropAspectRatio = 1.90
        };
        Assert.False(ImageAspectFilter.IsAcceptableBytes(CreatePngWithDimensions(2560, 1080), custom, out _));
        Assert.True(ImageAspectFilter.IsAcceptableBytes(CreatePngWithDimensions(1920, 1080), custom, out _));

        // Disabled filter accepts even ultra-wide banners
        var disabled = new AppSettings { FilterNonWallpaperAspectRatios = false };
        Assert.True(ImageAspectFilter.IsAcceptableBytes(CreatePngWithDimensions(1000, 185), disabled, out _));
    }

    [Fact]
    public async Task ResolveBackdropAsync_SkipsBannerIndexAndFallsBackToSecondBackdropWithoutCachingBanner()
    {
        Directory.CreateDirectory(_tempDir);
        var bannerBytes = CreatePngWithDimensions(1000, 185);
        var validWallpaperBytes = CreatePngWithDimensions(1920, 1080);

        var requestedUrls = new List<string>();
        var handler = CreateAuthAndImageHandler(path =>
        {
            requestedUrls.Add(path);
            if (path.Contains("/Images/Backdrop/0", StringComparison.Ordinal))
            {
                return bannerBytes;
            }

            return validWallpaperBytes;
        });

        var (imageService, _, cache) = await CreateAuthenticatedImageServiceAsync(handler);
        var item = new MediaItem
        {
            Id = "movie-multi",
            Name = "Dune",
            Type = "Movie",
            BackdropImageTags = ["banner-tag", "wallpaper-tag"]
        };

        var result = await imageService.ResolveBackdropAsync(item);

        Assert.Equal(BackdropResolveStatus.Success, result.Status);
        Assert.NotNull(result.ImageInfo);
        Assert.Equal(1, result.ImageInfo!.ImageIndex);
        Assert.Equal("wallpaper-tag", result.ImageInfo.Tag);
        Assert.Null(cache.TryGetCachedFile("movie-multi", 0, "banner-tag"));
        Assert.NotNull(cache.TryGetCachedFile("movie-multi", 1, "wallpaper-tag"));
        Assert.Equal(2, requestedUrls.Count(u => u.Contains("/Images/Backdrop/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ResolveBackdropAsync_RejectsCachedBannerAndDownloadsValidAlternate()
    {
        Directory.CreateDirectory(_tempDir);
        var bannerBytes = CreatePngWithDimensions(1000, 185);
        var validWallpaperBytes = CreatePngWithDimensions(1920, 1080);

        var handler = CreateAuthAndImageHandler(_ => validWallpaperBytes);
        var (imageService, _, cache) = await CreateAuthenticatedImageServiceAsync(handler);

        // Pre-seed index 0 in cache with a banner from before filtering existed
        await cache.SaveToCacheAsync("movie-cached-banner", 0, "tag0", bannerBytes, ".png");

        var item = new MediaItem
        {
            Id = "movie-cached-banner",
            Name = "Blade Runner",
            BackdropImageTags = ["tag0", "tag1"]
        };

        var result = await imageService.ResolveBackdropAsync(item);

        Assert.Equal(BackdropResolveStatus.Success, result.Status);
        Assert.Equal(1, result.ImageInfo!.ImageIndex);
    }

    [Fact]
    public async Task ResolveBackdropAsync_SkipsItemWhenAllBackdropsFailAspectFilterEvenWithFallbackConfigured()
    {
        Directory.CreateDirectory(_tempDir);
        var bannerBytes = CreatePngWithDimensions(1000, 185);
        var portraitBytes = CreatePngWithDimensions(600, 900);

        var handler = CreateAuthAndImageHandler(path =>
            path.Contains("/Images/Backdrop/0", StringComparison.Ordinal) ? bannerBytes : portraitBytes);

        var (imageService, settings, cache) = await CreateAuthenticatedImageServiceAsync(handler);
        var fallbackFile = Path.Combine(_tempDir, "fallback.png");
        await File.WriteAllBytesAsync(fallbackFile, CreatePngWithDimensions(1920, 1080));
        settings.Current.FallbackImagePath = fallbackFile;

        var item = new MediaItem
        {
            Id = "banner-only",
            Name = "Banner Show",
            BackdropImageTags = ["b0", "p1"]
        };

        var result = await imageService.ResolveBackdropAsync(item);

        Assert.Equal(BackdropResolveStatus.SkippedNoBackdrop, result.Status);
        Assert.False(result.HasImage);
        Assert.Equal(0, cache.GetStats().FileCount);

        // Disabling the filter allows the previously rejected banner
        settings.Current.FilterNonWallpaperAspectRatios = false;
        var acceptedWhenDisabled = await imageService.ResolveBackdropAsync(item);
        Assert.Equal(BackdropResolveStatus.Success, acceptedWhenDisabled.Status);
        Assert.True(acceptedWhenDisabled.HasImage);
    }

    [Fact]
    public async Task SlideshowService_AutomaticallySkipsItemWhoseBackdropsAreAllBanners()
    {
        Directory.CreateDirectory(_tempDir);
        var bannerBytes = CreatePngWithDimensions(1000, 185);
        var validWallpaperBytes = CreatePngWithDimensions(1920, 1080);

        var handler = CreateAuthAndImageHandler(path =>
            path.Contains("/Items/banner-item/", StringComparison.Ordinal) ? bannerBytes : validWallpaperBytes);

        var (imageService, settings, _) = await CreateAuthenticatedImageServiceAsync(handler);
        settings.Current.OrderMode = SlideshowOrderMode.Sequential;

        var timer = new FakeTimer();
        var slideshow = new SlideshowService(imageService, settings, timer, new NullLoggingService());
        slideshow.SetItems(
        [
            new MediaItem { Id = "banner-item", Name = "Only Banner", BackdropImageTags = ["b0"] },
            new MediaItem { Id = "valid-item", Name = "Valid Movie", BackdropImageTags = ["w0"] }
        ]);

        await slideshow.StartAsync();

        Assert.Equal("valid-item", slideshow.CurrentState.CurrentItemId);
        Assert.True(File.Exists(slideshow.CurrentState.CurrentImagePath));
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

    private static StubHttpMessageHandler CreateAuthAndImageHandler(Func<string, byte[]> imageBytesSelector)
    {
        return new StubHttpMessageHandler(req =>
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

            if (path.Contains("/Images/Backdrop/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(imageBytesSelector(path))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    public static byte[] CreatePngWithDimensions(int width, int height)
    {
        var bytes = new byte[64];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private static byte[] CreateJpegWithDimensions(int width, int height)
    {
        var bytes = new byte[64];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), 16);
        bytes[20] = 0xFF;
        bytes[21] = 0xC0;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22, 2), 17);
        bytes[24] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(25, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(27, 2), (ushort)width);
        return bytes;
    }

    private static byte[] CreateWebpVp8LWithDimensions(int width, int height)
    {
        var bytes = new byte[64];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        bytes[12] = (byte)'V';
        bytes[13] = (byte)'P';
        bytes[14] = (byte)'8';
        bytes[15] = (byte)'L';
        bytes[20] = 0x2F;
        var bits = ((uint)(width - 1) & 0x3FFF) | (((uint)(height - 1) & 0x3FFF) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21, 4), bits);
        return bytes;
    }

    private static byte[] CreateWebpVp8XWithDimensions(int width, int height)
    {
        var bytes = new byte[64];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        bytes[12] = (byte)'V';
        bytes[13] = (byte)'P';
        bytes[14] = (byte)'8';
        bytes[15] = (byte)'X';
        var w = width - 1;
        var h = height - 1;
        bytes[24] = (byte)(w & 0xFF);
        bytes[25] = (byte)((w >> 8) & 0xFF);
        bytes[26] = (byte)((w >> 16) & 0xFF);
        bytes[27] = (byte)(h & 0xFF);
        bytes[28] = (byte)((h >> 8) & 0xFF);
        bytes[29] = (byte)((h >> 16) & 0xFF);
        return bytes;
    }

    private static byte[] CreateBmpWithDimensions(int width, int height)
    {
        var bytes = new byte[64];
        bytes[0] = 0x42;
        bytes[1] = 0x4D;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), height);
        return bytes;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class FakeTimer : ISlideshowTimer
    {
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
