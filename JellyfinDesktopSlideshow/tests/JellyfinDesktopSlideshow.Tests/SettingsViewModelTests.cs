using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-vm-set-{Guid.NewGuid():N}");

    [Fact]
    public void UpdatingAllSevenSections_ClampsBoundsPersistsAndAppliesLiveServices()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var logger = new NullLoggingService();
        var settingsService = new SettingsService(logger, settingsPath);
        settingsService.Load();

        var slideshow = new SpySlideshowService();
        var overlay = new SpyOverlayService();
        var startup = new SpyStartupService();
        var launcher = new StubItemLauncher();

        var vm = new SettingsViewModel(
            new StubAuthService(),
            new StubLibraryService(),
            new CacheService(logger, Path.Combine(_tempDir, "cache")),
            slideshow,
            new StubWallpaperService(),
            launcher,
            overlay,
            startup,
            settingsService,
            logger);

        // Section 3: Slideshow & Wallpaper
        vm.SlideshowIntervalSeconds = 1; // clamped to min 5
        Assert.Equal(5, vm.SlideshowIntervalSeconds);
        Assert.Equal(1, slideshow.UpdateConfigurationCalls);

        vm.SelectedOfflineBehavior = OfflineBehaviorMode.KeepCurrentWallpaper;
        vm.RestoreWallpaperOnExit = true;

        // Section 4: Overlay appearance & toggle
        vm.OverlaySize = 200; // clamped to 96
        vm.OverlayOpacity = 0.05; // clamped to 0.2
        Assert.Equal(96, vm.OverlaySize);
        Assert.Equal(0.2, vm.OverlayOpacity);
        Assert.True(overlay.RefreshCalls >= 2);

        vm.ShowOverlayIcon = false;
        Assert.False(overlay.IsVisible);

        // Section 5: Playback Target & security validation
        vm.SelectedPlaybackTargetType = PlaybackTargetType.CustomUrlTemplate;
        vm.CustomUrlTemplate = "cmd.exe /c whoami";
        Assert.False(string.IsNullOrWhiteSpace(vm.CustomUrlValidationMessage));
        Assert.Null(settingsService.Current.PlaybackTarget.CustomUrlTemplate);

        vm.CustomUrlTemplate = "{baseUrl}/web/index.html#!/details?id={itemId}";
        Assert.Equal(string.Empty, vm.CustomUrlValidationMessage);
        Assert.Equal("{baseUrl}/web/index.html#!/details?id={itemId}", settingsService.Current.PlaybackTarget.CustomUrlTemplate);

        // Section 6: Cache & Backdrop bounds
        vm.MaxCacheSizeMb = 10; // clamped to 32
        vm.MaxCacheAgeDays = 999; // clamped to 365
        vm.RandomBackdropPerItem = true;
        Assert.Equal(32, vm.MaxCacheSizeMb);
        Assert.Equal(365, vm.MaxCacheAgeDays);

        // Section 7: Startup & Tray behavior
        vm.StartMinimizedToTray = true;
        vm.LaunchOnWindowsStartup = true;
        vm.MinimizeToTrayOnClose = false;
        Assert.True(startup.LastEnabled);
        Assert.True(startup.LastMinimized);

        // Reload from JSON and verify persistence
        var reloaded = new SettingsService(logger, settingsPath).Load();
        Assert.Equal(5, reloaded.SlideshowIntervalSeconds);
        Assert.Equal(OfflineBehaviorMode.KeepCurrentWallpaper, reloaded.OfflineBehavior);
        Assert.True(reloaded.RestoreWallpaperOnExit);
        Assert.False(reloaded.ShowOverlayIcon);
        Assert.Equal(96, reloaded.OverlaySize);
        Assert.Equal(0.2, reloaded.OverlayOpacity);
        Assert.Equal(PlaybackTargetType.CustomUrlTemplate, reloaded.PlaybackTarget.TargetType);
        Assert.True(reloaded.LaunchOnWindowsStartup);
        Assert.True(reloaded.StartMinimizedToTray);
        Assert.False(reloaded.MinimizeToTrayOnClose);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class SpySlideshowService : ISlideshowService
    {
        public int UpdateConfigurationCalls { get; private set; }
        public SlideshowState CurrentState { get; } = new();
        public event EventHandler<SlideshowState>? StateChanged;
        public void SetItems(IReadOnlyList<MediaItem> items) => StateChanged?.Invoke(this, CurrentState);
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Pause() { }
        public void Resume() { }
        public void TogglePause() { }
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdateConfiguration() => UpdateConfigurationCalls++;
        public void Stop() { }
    }

    private sealed class SpyOverlayService : IOverlayService
    {
        public bool IsVisible { get; private set; } = true;
        public int RefreshCalls { get; private set; }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void RefreshFromSettings() => RefreshCalls++;
        public void Close() => IsVisible = false;
    }

    private sealed class SpyStartupService : IStartupService
    {
        public bool LastEnabled { get; private set; }
        public bool LastMinimized { get; private set; }
        public bool IsStartupEnabled() => LastEnabled;
        public bool SetStartupEnabled(bool enabled, bool startMinimized)
        {
            LastEnabled = enabled;
            LastMinimized = startMinimized;
            return true;
        }
    }

    private sealed class StubAuthService : IAuthenticationService
    {
        public ConnectionSessionState CurrentSession { get; } = new() { IsAuthenticated = false };
        public Task<ServerPublicInfo> TestConnectionAsync(string serverUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionSessionState> LoginAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionSessionState> TryRestoreSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentSession);
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubLibraryService : ILibraryService
    {
        public Task<IReadOnlyList<JellyfinLibrary>> GetAccessibleLibrariesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JellyfinLibrary>>([]);
        public Task<IReadOnlyList<MediaItem>> GetMediaItemsAsync(IReadOnlyList<string> libraryIds, IReadOnlyList<string> includeItemTypes, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MediaItem>>([]);
    }

    private sealed class StubWallpaperService : IWallpaperService
    {
        public string? LastAppliedWallpaperPath => null;
        public Task<WallpaperApplyResult> ApplyWallpaperAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));
        public Task<WallpaperApplyResult> ReapplyCurrentWallpaperAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));
        public Task<WallpaperApplyResult> RestoreOriginalWallpaperAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));
        public string GetMonitorsSummary() => "مانیتور 1 (اصلی): 1920×1080";
    }

    private sealed class StubItemLauncher : IItemLauncher
    {
        public Task<ItemLaunchResult> OpenItemAsync(MediaItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new ItemLaunchResult(true, "OK"));
        public bool OpenValidatedBrowserUrl(string url) => true;
        public Uri BuildWebDetailsUri(Uri serverUri, string itemId, string? serverId) => new($"{serverUri}");
        public bool TryValidateCustomUrlTemplate(string? template, out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(template) || !template.StartsWith("{baseUrl}", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "الگو نامعتبر است.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
