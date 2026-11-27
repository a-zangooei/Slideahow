using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class AppCleanupServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-cleanup-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetFootprintAndCacheOnlyCleanup_PreservesSettingsCredentialsAndOsTraces()
    {
        var paths = new AppDataPaths(_tempDir);
        SeedFullAppFootprint(paths, out var settingsService, out var cacheService, out var currentItemStore, out var secureStorage);

        var auth = new SpyAuthService();
        var slideshow = new SpySlideshowService();
        var wallpaper = new SpyWallpaperService { HasCapturedOriginalWallpaper = true };
        var startup = new SpyStartupService { Enabled = true };
        var shortcut = new SpyDesktopShortcutService { Present = true };
        var overlay = new SpyOverlayService { IsVisible = true };
        var localization = CreateTestLocalization("en-US");

        var cleanup = new AppCleanupService(
            paths,
            cacheService,
            wallpaper,
            slideshow,
            auth,
            secureStorage,
            startup,
            shortcut,
            settingsService,
            new NullLoggingService(),
            overlay,
            currentItemStore,
            localization);

        var initial = cleanup.GetFootprint();
        Assert.True(initial.RootDirectoryExists);
        Assert.Equal(2, initial.BackdropCacheFiles);
        Assert.Equal(1, initial.WallpaperStagingFiles);
        Assert.Equal(1, initial.LogFiles);
        Assert.Equal(1, initial.BackupFiles);
        Assert.True(initial.HasSettingsFile);
        Assert.True(initial.HasWallpaperStateFile);
        Assert.True(initial.HasCurrentItemFile);
        Assert.True(initial.HasIconFile);
        Assert.True(initial.HasSavedCredential);
        Assert.True(initial.HasStartupRegistryEntry);
        Assert.True(initial.HasDesktopShortcut);
        Assert.True(initial.HasAnyFootprint);

        var result = await cleanup.CleanAsync(new AppCleanupOptions
        {
            ClearBackdropCache = true,
            ClearWallpaperStagingCache = true
        });

        Assert.True(result.Succeeded);
        Assert.False(result.RootDirectoryRemoved);
        Assert.Equal(0, slideshow.StopCalls);
        Assert.Equal(0, wallpaper.RestoreCalls);
        Assert.Equal(0, auth.LogoutCalls);

        var after = cleanup.GetFootprint();
        Assert.Equal(0, after.BackdropCacheFiles);
        Assert.Equal(0, after.WallpaperStagingFiles);
        Assert.Equal(1, after.LogFiles);
        Assert.Equal(1, after.BackupFiles);
        Assert.True(after.HasSettingsFile);
        Assert.True(after.HasSavedCredential);
        Assert.True(after.HasStartupRegistryEntry);
        Assert.True(after.HasDesktopShortcut);
    }

    [Fact]
    public async Task ClearLogsAndHiddenFiles_RemovesLogsIconsAndCurrentItemWithoutRecreatingLogsDirectory()
    {
        var paths = new AppDataPaths(_tempDir);
        SeedFullAppFootprint(paths, out var settingsService, out var cacheService, out var currentItemStore, out var secureStorage);

        var strayTmp = Path.Combine(paths.RootDirectory, "pending.tmp");
        await File.WriteAllTextAsync(strayTmp, "tmp");

        var cleanup = new AppCleanupService(
            paths,
            cacheService,
            new SpyWallpaperService(),
            new SpySlideshowService(),
            new SpyAuthService(),
            secureStorage,
            new SpyStartupService { Enabled = true },
            new SpyDesktopShortcutService { Present = true },
            settingsService,
            new NullLoggingService(),
            new SpyOverlayService(),
            currentItemStore,
            CreateTestLocalization("fa-IR"));

        var result = await cleanup.CleanAsync(new AppCleanupOptions
        {
            ClearLogsAndHiddenFiles = true
        });

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(paths.LogsDirectory));
        Assert.False(Directory.Exists(paths.IconsDirectory));
        Assert.False(File.Exists(paths.CurrentWallpaperItemFilePath));
        Assert.False(File.Exists(strayTmp));

        // Settings and OS traces remain untouched
        Assert.True(File.Exists(paths.SettingsFilePath));
        Assert.True(File.Exists(paths.WallpaperStateFilePath));
    }

    [Fact]
    public async Task FullWipe_RestoresWallpaperBeforeDeletingBackupDeletesAllTracesAndRemovesRootDirectory()
    {
        var paths = new AppDataPaths(_tempDir);
        SeedFullAppFootprint(paths, out var settingsService, out var cacheService, out var currentItemStore, out var secureStorage);

        var auth = new SpyAuthService();
        var slideshow = new SpySlideshowService();
        var startup = new SpyStartupService { Enabled = true };
        var shortcut = new SpyDesktopShortcutService { Present = true };
        var overlay = new SpyOverlayService { IsVisible = true };
        var wallpaper = new SpyWallpaperService
        {
            HasCapturedOriginalWallpaper = true,
            OnRestoreCallback = () =>
            {
                // Backup and wallpaper-state MUST still exist when original wallpaper restore runs.
                Assert.True(File.Exists(paths.WallpaperStateFilePath));
                Assert.True(Directory.GetFiles(paths.BackupDirectory).Length > 0);
            }
        };

        var cleanup = new AppCleanupService(
            paths,
            cacheService,
            wallpaper,
            slideshow,
            auth,
            secureStorage,
            startup,
            shortcut,
            settingsService,
            new NullLoggingService(),
            overlay,
            currentItemStore,
            CreateTestLocalization("en-US"));

        var result = await cleanup.CleanAsync(AppCleanupOptions.FullWipe(restoreOriginalWallpaper: true));

        Assert.True(result.Succeeded);
        Assert.True(result.RootDirectoryRemoved);
        Assert.False(Directory.Exists(paths.RootDirectory));
        Assert.False(File.Exists(paths.SettingsFilePath));
        Assert.Equal(1, slideshow.StopCalls);
        Assert.Equal(1, wallpaper.RestoreCalls);
        Assert.Equal(1, auth.LogoutCalls);
        Assert.Null(secureStorage.ReadSecret(settingsService.Current.TokenCredentialKey));
        Assert.False(startup.Enabled);
        Assert.False(shortcut.Present);
        Assert.False(overlay.IsVisible);

        var after = cleanup.GetFootprint();
        Assert.False(after.HasAnyFootprint);
    }

    [Fact]
    public async Task SettingsViewModel_CleanupCommands_UpdateFootprintsAndResetStateWithoutRecreatingSettingsFile()
    {
        var paths = new AppDataPaths(_tempDir);
        SeedFullAppFootprint(paths, out var settingsService, out var cacheService, out var currentItemStore, out var secureStorage);

        var auth = new SpyAuthService();
        var slideshow = new SpySlideshowService();
        var wallpaper = new SpyWallpaperService { HasCapturedOriginalWallpaper = true };
        var startup = new SpyStartupService { Enabled = true };
        var shortcut = new SpyDesktopShortcutService { Present = true };
        var overlay = new SpyOverlayService { IsVisible = true };
        var localization = CreateTestLocalization("en-US");
        var logger = new NullLoggingService();

        var cleanup = new AppCleanupService(
            paths,
            cacheService,
            wallpaper,
            slideshow,
            auth,
            secureStorage,
            startup,
            shortcut,
            settingsService,
            logger,
            overlay,
            currentItemStore,
            localization);

        var vm = new SettingsViewModel(
            auth,
            new StubLibraryService(),
            cacheService,
            slideshow,
            wallpaper,
            new StubItemLauncher(),
            overlay,
            shortcut,
            startup,
            settingsService,
            logger,
            cleanup,
            localization);

        Assert.True(vm.CleanupAvailable);
        Assert.True(vm.HasAnyCleanupFootprint);
        Assert.Contains("2", vm.CleanupCacheSummaryLine);

        var syncedCount = 0;
        vm.StateSynchronized += (_, _) => syncedCount++;

        var cacheDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSync(object? _, EventArgs __) => cacheDone.TrySetResult();
        vm.StateSynchronized += OnSync;
        vm.QuickClearImageCacheCommand.Execute(null);
        await cacheDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.StateSynchronized -= OnSync;

        var cacheResult = cleanup.GetFootprint();
        Assert.Equal(0, cacheResult.BackdropCacheFiles);
        Assert.Equal(0, cacheResult.WallpaperStagingFiles);
        Assert.True(vm.HasAnyCleanupFootprint);

        vm.CleanBackdropCache = false;
        vm.CleanWallpaperStagingCache = false;
        vm.CleanLogsAndHiddenFiles = true;
        var selectedResult = await vm.ExecuteSelectedCleanupAsync();
        Assert.NotNull(selectedResult);
        Assert.True(selectedResult!.Succeeded);
        Assert.False(Directory.Exists(paths.LogsDirectory));

        var wipeResult = await vm.ExecuteFullWipeAsync();
        Assert.NotNull(wipeResult);
        Assert.True(wipeResult!.Succeeded);
        Assert.True(wipeResult.RootDirectoryRemoved);
        Assert.False(Directory.Exists(paths.RootDirectory));
        Assert.False(File.Exists(paths.SettingsFilePath));
        Assert.False(vm.HasAnyCleanupFootprint);
        Assert.False(vm.IsAuthenticated);
        Assert.False(vm.LaunchOnWindowsStartup);
        Assert.True(syncedCount >= 2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static LocalizationService CreateTestLocalization(string code)
    {
        var provider = new JsonLanguagePackProvider(
            typeof(LocalizationService).Assembly,
            Path.Combine(Path.GetTempPath(), $"jf-no-ext-{Guid.NewGuid():N}"));
        return new LocalizationService(provider, code);
    }

    private static void SeedFullAppFootprint(
        AppDataPaths paths,
        out SettingsService settingsService,
        out CacheService cacheService,
        out CurrentWallpaperItemStore currentItemStore,
        out InMemorySecureStorageService secureStorage)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        Directory.CreateDirectory(paths.BackdropCacheDirectory);
        Directory.CreateDirectory(paths.WallpaperStagingDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
        Directory.CreateDirectory(paths.BackupDirectory);
        Directory.CreateDirectory(paths.IconsDirectory);

        var logger = new NullLoggingService();
        settingsService = new SettingsService(logger, paths.SettingsFilePath);
        var settings = settingsService.Load();
        settings.ServerUrl = "http://localhost:8096";
        settings.ServerId = "srv-1";
        settings.Username = "tester";
        settings.SelectedLibraryIds = ["lib-1"];
        settings.LaunchOnWindowsStartup = true;
        settings.ShowDesktopShortcutIcon = true;
        settingsService.Save(settings);

        cacheService = new CacheService(logger, paths.BackdropCacheDirectory);
        File.WriteAllBytes(Path.Combine(paths.BackdropCacheDirectory, "backdrop1.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        File.WriteAllBytes(Path.Combine(paths.BackdropCacheDirectory, "backdrop2.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        File.WriteAllBytes(Path.Combine(paths.WallpaperStagingDirectory, "desktop_wallpaper.png"), [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(paths.LogsDirectory, "app-20260101.log"), "sample log line");
        File.WriteAllBytes(Path.Combine(paths.BackupDirectory, "original_transcoded.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        File.WriteAllText(paths.WallpaperStateFilePath, """{"OriginalPath":"C:\\original.jpg"}""");
        File.WriteAllBytes(paths.DesktopShortcutIconFilePath, [0x00, 0x00, 0x01, 0x00]);

        currentItemStore = new CurrentWallpaperItemStore(logger, paths.CurrentWallpaperItemFilePath);
        currentItemStore.Save(new MediaItem { Id = "item-1", Name = "Inception", Type = "Movie", ProductionYear = 2010 });

        secureStorage = new InMemorySecureStorageService();
        secureStorage.SaveSecret(settings.TokenCredentialKey, "tester", "secret-token-value");
    }

    private sealed class SpySlideshowService : ISlideshowService
    {
        public int StopCalls { get; private set; }
        public SlideshowState CurrentState { get; } = new();
        public event EventHandler<SlideshowState>? StateChanged;
        public void SetItems(IReadOnlyList<MediaItem> items) => StateChanged?.Invoke(this, CurrentState);
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Pause() { }
        public void Resume() { }
        public void TogglePause() { }
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdateConfiguration() { }
        public void Stop() => StopCalls++;
    }

    private sealed class SpyWallpaperService : IWallpaperService
    {
        public string? LastAppliedWallpaperPath { get; set; }
        public bool HasCapturedOriginalWallpaper { get; set; }
        public int RestoreCalls { get; private set; }
        public Action? OnRestoreCallback { get; init; }

        public Task<WallpaperApplyResult> ApplyWallpaperAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK", imagePath));

        public Task<WallpaperApplyResult> ReapplyCurrentWallpaperAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));

        public Task<WallpaperApplyResult> RestoreOriginalWallpaperAsync(CancellationToken cancellationToken = default)
        {
            OnRestoreCallback?.Invoke();
            RestoreCalls++;
            HasCapturedOriginalWallpaper = false;
            LastAppliedWallpaperPath = null;
            return Task.FromResult(new WallpaperApplyResult(true, "Restored"));
        }

        public string GetMonitorsSummary() => "Display 1: 1920x1080";
    }

    private sealed class SpyAuthService : IAuthenticationService
    {
        public int LogoutCalls { get; private set; }
        public ConnectionSessionState CurrentSession { get; private set; } = new()
        {
            IsAuthenticated = true,
            ServerName = "HomeNAS",
            ServerVersion = "10.10.3",
            Username = "tester"
        };

        public Task<ServerPublicInfo> TestConnectionAsync(string serverUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionSessionState> LoginWithApiKeyAsync(string serverUrl, string apiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionSessionState> LoginAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionSessionState> TryRestoreSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentSession);

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCalls++;
            CurrentSession = new ConnectionSessionState { IsAuthenticated = false };
            return Task.CompletedTask;
        }
    }

    private sealed class SpyStartupService : IStartupService
    {
        public bool Enabled { get; set; }
        public bool IsStartupEnabled() => Enabled;
        public bool SetStartupEnabled(bool enabled, bool startMinimized)
        {
            Enabled = enabled;
            return true;
        }
    }

    private sealed class SpyDesktopShortcutService : IDesktopShortcutService
    {
        public bool Present { get; set; }
        public string ShortcutFilePath => @"C:\Desktop\Open in Jellyfin.lnk";
        public bool IsShortcutPresent() => Present;
        public DesktopShortcutUpdateResult CreateOrUpdateShortcut()
        {
            Present = true;
            return new DesktopShortcutUpdateResult(true, false, ShortcutFilePath, "OK");
        }

        public bool SetShortcutEnabled(bool enabled)
        {
            Present = enabled;
            return true;
        }
    }

    private sealed class SpyOverlayService : IOverlayService
    {
        public bool IsVisible { get; set; }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void RefreshFromSettings() { }
        public void Close() => IsVisible = false;
    }

    private sealed class StubLibraryService : ILibraryService
    {
        public Task<IReadOnlyList<JellyfinLibrary>> GetAccessibleLibrariesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JellyfinLibrary>>([]);
        public Task<IReadOnlyList<MediaItem>> GetMediaItemsAsync(IReadOnlyList<string> libraryIds, IReadOnlyList<string> includeItemTypes, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MediaItem>>([]);
    }

    private sealed class StubItemLauncher : IItemLauncher
    {
        public Task<ItemLaunchResult> OpenItemAsync(MediaItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new ItemLaunchResult(true, "OK"));
        public bool OpenValidatedBrowserUrl(string url) => true;
        public Uri BuildWebDetailsUri(Uri serverUri, string itemId, string? serverId) => serverUri;
        public bool TryValidateCustomUrlTemplate(string? template, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }
}
