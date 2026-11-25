using System.IO;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class WallpaperServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-wall-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task ApplyWallpaperAsync_AppliesValidImageWithConfiguredFitMode()
    {
        Directory.CreateDirectory(_tempDir);
        var imagePath = Path.Combine(_tempDir, "bg.png");
        await File.WriteAllBytesAsync(imagePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter();
        var (service, settings) = CreateService(adapter);
        settings.Current.WallpaperFitMode = WallpaperFitMode.Span;

        var result = await service.ApplyWallpaperAsync(imagePath);

        Assert.True(result.Succeeded);
        Assert.Equal(imagePath, service.LastAppliedWallpaperPath);
        Assert.Equal(WallpaperFitMode.Span, adapter.LastFitMode);
    }

    [Fact]
    public async Task ApplyWallpaperAsync_RejectsMissingOrInvalidFileWithoutCallingPlatform()
    {
        Directory.CreateDirectory(_tempDir);
        var adapter = new FakeWallpaperPlatformAdapter();
        var (service, _) = CreateService(adapter);

        var result = await service.ApplyWallpaperAsync(Path.Combine(_tempDir, "missing.png"));

        Assert.False(result.Succeeded);
        Assert.Equal(0, adapter.SetCalls);
    }

    [Fact]
    public async Task ApplyWallpaperAsync_HandlesWin32FailureGracefully()
    {
        Directory.CreateDirectory(_tempDir);
        var imagePath = Path.Combine(_tempDir, "bg.png");
        await File.WriteAllBytesAsync(imagePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            NextResult = new PlatformWallpaperResult(false, 5, "Access denied")
        };
        var (service, _) = CreateService(adapter);

        var result = await service.ApplyWallpaperAsync(imagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Win32ErrorCode);
    }

    [Fact]
    public void GetMonitorsSummary_FormatsMultipleResolutions()
    {
        Directory.CreateDirectory(_tempDir);
        var adapter = new FakeWallpaperPlatformAdapter
        {
            Monitors =
            [
                new MonitorResolutionInfo(@"\\.\DISPLAY1", 1920, 1080, true),
                new MonitorResolutionInfo(@"\\.\DISPLAY2", 2560, 1440, false)
            ]
        };

        var (service, _) = CreateService(adapter);
        var summary = service.GetMonitorsSummary();

        Assert.Contains("1920×1080", summary);
        Assert.Contains("2560×1440", summary);
    }

    [Fact]
    public async Task RestoreOriginalWallpaperAsync_RestoresCapturedDesktopPath()
    {
        Directory.CreateDirectory(_tempDir);
        var originalPath = Path.Combine(_tempDir, "original.png");
        var slidePath = Path.Combine(_tempDir, "slide.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slidePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter { CurrentWallpaper = originalPath };
        var (service, _) = CreateService(adapter);

        await service.ApplyWallpaperAsync(slidePath);
        var restore = await service.RestoreOriginalWallpaperAsync();

        Assert.True(restore.Succeeded);
        Assert.Equal(originalPath, adapter.LastSetPath);
    }

    private (WallpaperService Service, SettingsService Settings) CreateService(FakeWallpaperPlatformAdapter adapter)
    {
        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, Path.Combine(_tempDir, "settings.json"));
        settings.Load();
        var service = new WallpaperService(adapter, settings, logger, Path.Combine(_tempDir, "staging"));
        return (service, settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class FakeWallpaperPlatformAdapter : IWallpaperPlatformAdapter
    {
        public string? CurrentWallpaper { get; set; }
        public int SetCalls { get; private set; }
        public string? LastSetPath { get; private set; }
        public WallpaperFitMode LastFitMode { get; private set; }
        public PlatformWallpaperResult NextResult { get; set; } = new(true, 0, "OK");
        public IReadOnlyList<MonitorResolutionInfo> Monitors { get; set; } =
            [new MonitorResolutionInfo(@"\\.\DISPLAY1", 1920, 1080, true)];

        public string? GetCurrentDesktopWallpaper() => CurrentWallpaper;

        public string EnsureCompatibleImageFormat(string sourceImagePath, string stagingDirectory) => sourceImagePath;

        public PlatformWallpaperResult SetDesktopWallpaper(string imagePath, WallpaperFitMode fitMode)
        {
            SetCalls++;
            LastSetPath = imagePath;
            LastFitMode = fitMode;
            return NextResult;
        }

        public IReadOnlyList<MonitorResolutionInfo> GetMonitors() => Monitors;
    }
}
