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
        Assert.True(service.HasCapturedOriginalWallpaper);
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
        Assert.False(service.HasCapturedOriginalWallpaper);
    }

    [Fact]
    public async Task ApplyWallpaperAsync_HandlesWin32FailureGracefullyAndRollsBackFreshCapture()
    {
        Directory.CreateDirectory(_tempDir);
        var originalPath = Path.Combine(_tempDir, "original.png");
        var imagePath = Path.Combine(_tempDir, "bg.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(imagePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = originalPath,
            NextResult = new PlatformWallpaperResult(false, 5, "Access denied")
        };
        var (service, _) = CreateService(adapter);

        var result = await service.ApplyWallpaperAsync(imagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Win32ErrorCode);
        Assert.False(service.HasCapturedOriginalWallpaper);
        Assert.Null(service.LastAppliedWallpaperPath);
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
    public async Task RestoreOriginalWallpaperAsync_RestoresCapturedDesktopPathAndRawTileStyle()
    {
        Directory.CreateDirectory(_tempDir);
        var originalPath = Path.Combine(_tempDir, "original.png");
        var slidePath = Path.Combine(_tempDir, "slide.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slidePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = originalPath,
            CurrentWallpaperStyle = "0",
            CurrentTileWallpaper = "1", // Tile mode (not represented in WallpaperFitMode)
            CurrentBackgroundMode = WindowsBackgroundMode.Picture
        };
        var (service, settings) = CreateService(adapter);
        settings.Current.WallpaperFitMode = WallpaperFitMode.Span;

        await service.ApplyWallpaperAsync(slidePath);
        Assert.Equal("22", adapter.CurrentWallpaperStyle);
        Assert.Equal("0", adapter.CurrentTileWallpaper);

        var restore = await service.RestoreOriginalWallpaperAsync();

        Assert.True(restore.Succeeded);
        Assert.Equal(1, adapter.RestoreCalls);
        Assert.Equal(originalPath, adapter.LastRestorePath);
        Assert.Equal("0", adapter.LastRestoredWallpaperStyle);
        Assert.Equal("1", adapter.LastRestoredTileWallpaper);
        Assert.Equal(originalPath, adapter.CurrentWallpaper);
        Assert.Null(service.LastAppliedWallpaperPath);
        Assert.False(service.HasCapturedOriginalWallpaper);
    }

    [Fact]
    public async Task ApplyWallpaperAsync_MultipleChanges_PreservesInitialSnapshotAcrossFitChanges()
    {
        Directory.CreateDirectory(_tempDir);
        var originalPath = Path.Combine(_tempDir, "original.png");
        var slide1Path = Path.Combine(_tempDir, "slide1.png");
        var slide2Path = Path.Combine(_tempDir, "slide2.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slide1Path, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slide2Path, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = originalPath,
            CurrentWallpaperStyle = "6",
            CurrentTileWallpaper = "0"
        };
        var (service, settings) = CreateService(adapter);

        settings.Current.WallpaperFitMode = WallpaperFitMode.Fill;
        await service.ApplyWallpaperAsync(slide1Path);

        settings.Current.WallpaperFitMode = WallpaperFitMode.Stretch;
        await service.ReapplyCurrentWallpaperAsync();

        settings.Current.WallpaperFitMode = WallpaperFitMode.Center;
        await service.ApplyWallpaperAsync(slide2Path);

        var restore = await service.RestoreOriginalWallpaperAsync();

        Assert.True(restore.Succeeded);
        Assert.Equal(originalPath, adapter.LastRestorePath);
        Assert.Equal("6", adapter.LastRestoredWallpaperStyle);
        Assert.Equal("0", adapter.LastRestoredTileWallpaper);
    }

    [Fact]
    public async Task ApplyWallpaperAsync_WhenCurrentDesktopIsManagedByApp_DoesNotSaveSelfAsOriginal()
    {
        Directory.CreateDirectory(_tempDir);
        var stagingDir = Path.Combine(_tempDir, "staging");
        var cacheDir = Path.Combine(_tempDir, "cache");
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(cacheDir);

        var stagedSelfPath = Path.Combine(stagingDir, "desktop_wallpaper.png");
        var slidePath = Path.Combine(cacheDir, "item1_0_tag.png");
        await File.WriteAllBytesAsync(stagedSelfPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slidePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = stagedSelfPath,
            CurrentWallpaperStyle = "10",
            CurrentTileWallpaper = "0"
        };
        var (service, _) = CreateService(adapter, stagingDir: stagingDir, cacheDir: cacheDir);

        var applyResult = await service.ApplyWallpaperAsync(slidePath);
        var restoreResult = await service.RestoreOriginalWallpaperAsync();

        Assert.True(applyResult.Succeeded);
        Assert.False(service.HasCapturedOriginalWallpaper);
        Assert.False(restoreResult.Succeeded);
        Assert.Equal(0, adapter.RestoreCalls);
    }

    [Fact]
    public async Task UnexpectedExit_PreservesPersistedOriginalStateAcrossRestartAndRestores()
    {
        Directory.CreateDirectory(_tempDir);
        var stateFilePath = Path.Combine(_tempDir, "wallpaper-state.json");
        var originalPath = Path.Combine(_tempDir, "original.png");
        var slide1Path = Path.Combine(_tempDir, "slide1.png");
        var slide2Path = Path.Combine(_tempDir, "slide2.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slide1Path, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slide2Path, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = originalPath,
            CurrentWallpaperStyle = "0",
            CurrentTileWallpaper = "1"
        };

        // Session 1 applies slide1 and "crashes" (no restore call).
        var (session1, settings1) = CreateService(adapter, stateFilePath: stateFilePath);
        settings1.Current.WallpaperFitMode = WallpaperFitMode.Span;
        await session1.ApplyWallpaperAsync(slide1Path);
        Assert.Equal(slide1Path, adapter.CurrentWallpaper);

        // Session 2 starts after crash while Windows still displays slide1.png.
        var (session2, settings2) = CreateService(adapter, stateFilePath: stateFilePath);
        Assert.True(session2.HasCapturedOriginalWallpaper);
        settings2.Current.WallpaperFitMode = WallpaperFitMode.Fit;
        await session2.ApplyWallpaperAsync(slide2Path);

        var restore = await session2.RestoreOriginalWallpaperAsync();

        Assert.True(restore.Succeeded);
        Assert.Equal(originalPath, adapter.LastRestorePath);
        Assert.Equal("0", adapter.LastRestoredWallpaperStyle);
        Assert.Equal("1", adapter.LastRestoredTileWallpaper);
        Assert.False(session2.HasCapturedOriginalWallpaper);
    }

    [Fact]
    public async Task UnexpectedExit_ImmediateRestoreOnRestartWithoutApplyingNewSlide_RestoresOriginal()
    {
        Directory.CreateDirectory(_tempDir);
        var stateFilePath = Path.Combine(_tempDir, "wallpaper-state.json");
        var originalPath = Path.Combine(_tempDir, "original.png");
        var slide1Path = Path.Combine(_tempDir, "slide1.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slide1Path, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = originalPath,
            CurrentWallpaperStyle = "6",
            CurrentTileWallpaper = "0"
        };

        var (session1, _) = CreateService(adapter, stateFilePath: stateFilePath);
        await session1.ApplyWallpaperAsync(slide1Path);

        // New instance restores immediately before applying any new slide.
        var (session2, _) = CreateService(adapter, stateFilePath: stateFilePath);
        var restore = await session2.RestoreOriginalWallpaperAsync();

        Assert.True(restore.Succeeded);
        Assert.Equal(originalPath, adapter.LastRestorePath);
        Assert.Equal("6", adapter.LastRestoredWallpaperStyle);
        Assert.Equal("0", adapter.LastRestoredTileWallpaper);
    }

    [Fact]
    public async Task UnexpectedExit_WhenUserManuallyChangedWallpaperAfterCrash_DoesNotOverwriteManualChoice()
    {
        Directory.CreateDirectory(_tempDir);
        var stateFilePath = Path.Combine(_tempDir, "wallpaper-state.json");
        var originalPath = Path.Combine(_tempDir, "original.png");
        var slide1Path = Path.Combine(_tempDir, "slide1.png");
        var userManualPath = Path.Combine(_tempDir, "user_manual.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slide1Path, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(userManualPath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = originalPath,
            CurrentWallpaperStyle = "10",
            CurrentTileWallpaper = "0"
        };

        var (session1, _) = CreateService(adapter, stateFilePath: stateFilePath);
        await session1.ApplyWallpaperAsync(slide1Path);

        // User manually changes wallpaper in Windows while app is not running.
        adapter.CurrentWallpaper = userManualPath;
        adapter.CurrentWallpaperStyle = "2";
        adapter.CurrentTileWallpaper = "0";

        var (session2, _) = CreateService(adapter, stateFilePath: stateFilePath);
        var restore = await session2.RestoreOriginalWallpaperAsync();

        Assert.False(restore.Succeeded);
        Assert.Equal(0, adapter.RestoreCalls);
        Assert.Equal(userManualPath, adapter.CurrentWallpaper);
        Assert.False(session2.HasCapturedOriginalWallpaper);
    }

    [Fact]
    public async Task RestoreOriginalWallpaperAsync_VolatileTranscodedWallpaper_RestoresFromPreChangeBackup()
    {
        Directory.CreateDirectory(_tempDir);
        var themesDir = Path.Combine(_tempDir, "Microsoft", "Windows", "Themes");
        Directory.CreateDirectory(themesDir);
        var transcodedPath = Path.Combine(themesDir, "TranscodedWallpaper");
        var slidePath = Path.Combine(_tempDir, "slide.png");

        var originalJpgBytes = CreateValidJpgBytes(128);
        await File.WriteAllBytesAsync(transcodedPath, originalJpgBytes);
        await File.WriteAllBytesAsync(slidePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = transcodedPath,
            CurrentWallpaperStyle = "10",
            CurrentTileWallpaper = "0",
            VolatileThemePathToOverwrite = transcodedPath
        };
        var (service, _) = CreateService(adapter);

        await service.ApplyWallpaperAsync(slidePath);
        var restore = await service.RestoreOriginalWallpaperAsync();

        Assert.True(restore.Succeeded);
        Assert.NotNull(adapter.LastRestorePath);
        Assert.NotEqual(transcodedPath, adapter.LastRestorePath);
        Assert.True(File.Exists(adapter.LastRestorePath));
        Assert.Equal(originalJpgBytes, await File.ReadAllBytesAsync(adapter.LastRestorePath!));
    }

    [Fact]
    public async Task RestoreOriginalWallpaperAsync_VolatileTranscodedWallpaper_SubsequentSessionCanRecaptureAndRestore()
    {
        Directory.CreateDirectory(_tempDir);
        var themesDir = Path.Combine(_tempDir, "Microsoft", "Windows", "Themes");
        Directory.CreateDirectory(themesDir);
        var transcodedPath = Path.Combine(themesDir, "TranscodedWallpaper");
        var slide1Path = Path.Combine(_tempDir, "slide1.png");
        var slide2Path = Path.Combine(_tempDir, "slide2.png");
        var stateFilePath = Path.Combine(_tempDir, "wallpaper-state.json");

        var originalJpgBytes = CreateValidJpgBytes(128);
        await File.WriteAllBytesAsync(transcodedPath, originalJpgBytes);
        await File.WriteAllBytesAsync(slide1Path, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slide2Path, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = transcodedPath,
            CurrentWallpaperStyle = "10",
            CurrentTileWallpaper = "0",
            VolatileThemePathToOverwrite = transcodedPath
        };

        var (session1, _) = CreateService(adapter, stateFilePath: stateFilePath);
        await session1.ApplyWallpaperAsync(slide1Path);
        var restore1 = await session1.RestoreOriginalWallpaperAsync();
        Assert.True(restore1.Succeeded);

        var restoredBackupPath = adapter.CurrentWallpaper;
        Assert.NotNull(restoredBackupPath);
        Assert.True(File.Exists(restoredBackupPath));

        // Session 2 starts while Windows points to the restored backup file.
        var (session2, _) = CreateService(adapter, stateFilePath: stateFilePath);
        await session2.ApplyWallpaperAsync(slide2Path);
        Assert.True(session2.HasCapturedOriginalWallpaper);

        var restore2 = await session2.RestoreOriginalWallpaperAsync();
        Assert.True(restore2.Succeeded);
        Assert.Equal(restoredBackupPath, adapter.LastRestorePath);
        Assert.Equal(originalJpgBytes, await File.ReadAllBytesAsync(restoredBackupPath!));
    }

    [Fact]
    public async Task RestoreOriginalWallpaperAsync_WhenOriginalFileDeletedExternally_FailsWithoutDestructiveFallback()
    {
        Directory.CreateDirectory(_tempDir);
        var originalPath = Path.Combine(_tempDir, "original.png");
        var slidePath = Path.Combine(_tempDir, "slide.png");
        await File.WriteAllBytesAsync(originalPath, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slidePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter { CurrentWallpaper = originalPath };
        var (service, _) = CreateService(adapter);

        await service.ApplyWallpaperAsync(slidePath);
        File.Delete(originalPath);

        var restore = await service.RestoreOriginalWallpaperAsync();

        Assert.False(restore.Succeeded);
        Assert.Equal(0, adapter.RestoreCalls);
        Assert.False(service.HasCapturedOriginalWallpaper);
    }

    [Fact]
    public async Task RestoreOriginalWallpaperAsync_WhenOriginalWasSolidColor_DoesNotCallRestoreAndReportsLimitation()
    {
        Directory.CreateDirectory(_tempDir);
        var slidePath = Path.Combine(_tempDir, "slide.png");
        await File.WriteAllBytesAsync(slidePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = null,
            CurrentWallpaperStyle = "10",
            CurrentTileWallpaper = "0",
            CurrentBackgroundMode = WindowsBackgroundMode.SolidColor
        };
        var (service, _) = CreateService(adapter);

        await service.ApplyWallpaperAsync(slidePath);
        var restore = await service.RestoreOriginalWallpaperAsync();

        Assert.False(restore.Succeeded);
        Assert.Equal(0, adapter.RestoreCalls);
        Assert.Contains("Solid Color", restore.Message);
        Assert.Null(service.LastAppliedWallpaperPath);
    }

    [Theory]
    [InlineData(WindowsBackgroundMode.Slideshow, "Windows Slideshow")]
    [InlineData(WindowsBackgroundMode.Spotlight, "Windows Spotlight")]
    public async Task RestoreOriginalWallpaperAsync_WhenOriginalWasDynamicMode_RestoresFrameAndReportsLimitation(
        WindowsBackgroundMode mode,
        string expectedModeLabel)
    {
        Directory.CreateDirectory(_tempDir);
        var originalFrame = Path.Combine(_tempDir, "frame.png");
        var slidePath = Path.Combine(_tempDir, "slide.png");
        await File.WriteAllBytesAsync(originalFrame, CacheServiceTests.CreateValidPngBytes(128));
        await File.WriteAllBytesAsync(slidePath, CacheServiceTests.CreateValidPngBytes(128));

        var adapter = new FakeWallpaperPlatformAdapter
        {
            CurrentWallpaper = originalFrame,
            CurrentWallpaperStyle = "10",
            CurrentTileWallpaper = "0",
            CurrentBackgroundMode = mode
        };
        var (service, _) = CreateService(adapter);

        await service.ApplyWallpaperAsync(slidePath);
        var restore = await service.RestoreOriginalWallpaperAsync();

        Assert.True(restore.Succeeded);
        Assert.Equal(originalFrame, adapter.LastRestorePath);
        Assert.Contains(expectedModeLabel, restore.Message);
    }

    private (WallpaperService Service, SettingsService Settings) CreateService(
        FakeWallpaperPlatformAdapter adapter,
        string? stagingDir = null,
        string? stateFilePath = null,
        string? cacheDir = null)
    {
        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, Path.Combine(_tempDir, "settings.json"));
        settings.Load();
        var service = new WallpaperService(
            adapter,
            settings,
            logger,
            stagingDir ?? Path.Combine(_tempDir, "staging"),
            stateFilePath ?? Path.Combine(_tempDir, "wallpaper-state.json"),
            cacheDir ?? Path.Combine(_tempDir, "cache"));
        return (service, settings);
    }

    private static byte[] CreateValidJpgBytes(int totalLength)
    {
        var bytes = new byte[Math.Max(64, totalLength)];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        return bytes;
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
        public string? CurrentWallpaperStyle { get; set; } = "10";
        public string? CurrentTileWallpaper { get; set; } = "0";
        public WindowsBackgroundMode CurrentBackgroundMode { get; set; } = WindowsBackgroundMode.Picture;
        public string? VolatileThemePathToOverwrite { get; set; }

        public int SetCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public string? LastSetPath { get; private set; }
        public WallpaperFitMode LastFitMode { get; private set; }
        public string? LastRestorePath { get; private set; }
        public string? LastRestoredWallpaperStyle { get; private set; }
        public string? LastRestoredTileWallpaper { get; private set; }
        public PlatformWallpaperResult NextResult { get; set; } = new(true, 0, "OK");
        public IReadOnlyList<MonitorResolutionInfo> Monitors { get; set; } =
            [new MonitorResolutionInfo(@"\\.\DISPLAY1", 1920, 1080, true)];

        public DesktopWallpaperSnapshot GetCurrentDesktopWallpaperState()
            => new(CurrentWallpaper, CurrentWallpaperStyle, CurrentTileWallpaper, CurrentBackgroundMode);

        public string EnsureCompatibleImageFormat(string sourceImagePath, string stagingDirectory) => sourceImagePath;

        public PlatformWallpaperResult SetDesktopWallpaper(string imagePath, WallpaperFitMode fitMode)
        {
            SetCalls++;
            LastSetPath = imagePath;
            LastFitMode = fitMode;
            if (!NextResult.Succeeded)
            {
                return NextResult;
            }

            CurrentWallpaper = imagePath;
            (CurrentWallpaperStyle, CurrentTileWallpaper) = fitMode switch
            {
                WallpaperFitMode.Fill => ("10", "0"),
                WallpaperFitMode.Fit => ("6", "0"),
                WallpaperFitMode.Stretch => ("2", "0"),
                WallpaperFitMode.Center => ("0", "0"),
                WallpaperFitMode.Span => ("22", "0"),
                _ => ("10", "0")
            };
            CurrentBackgroundMode = WindowsBackgroundMode.Picture;

            if (!string.IsNullOrWhiteSpace(VolatileThemePathToOverwrite) && File.Exists(imagePath))
            {
                File.Copy(imagePath, VolatileThemePathToOverwrite, overwrite: true);
            }

            return NextResult;
        }

        public PlatformWallpaperResult RestoreDesktopWallpaper(string imagePath, string? wallpaperStyle, string? tileWallpaper)
        {
            RestoreCalls++;
            LastRestorePath = imagePath;
            LastRestoredWallpaperStyle = wallpaperStyle;
            LastRestoredTileWallpaper = tileWallpaper;
            if (!NextResult.Succeeded)
            {
                return NextResult;
            }

            CurrentWallpaper = imagePath;
            if (wallpaperStyle is not null)
            {
                CurrentWallpaperStyle = wallpaperStyle;
            }

            if (tileWallpaper is not null)
            {
                CurrentTileWallpaper = tileWallpaper;
            }

            return NextResult;
        }

        public IReadOnlyList<MonitorResolutionInfo> GetMonitors() => Monitors;
    }
}
