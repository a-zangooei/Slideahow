using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class DesktopShortcutWorkflowTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-shortcut-flow-{Guid.NewGuid():N}");

    [Fact]
    public void CurrentWallpaperItemStore_PersistsValidItemsAndRecoversSafelyFromCorruptFiles()
    {
        Directory.CreateDirectory(_tempDir);
        var storePath = Path.Combine(_tempDir, "current-wallpaper-item.json");
        var store = new CurrentWallpaperItemStore(new NullLoggingService(), storePath);

        Assert.Null(store.Load());

        var item = new MediaItem
        {
            Id = "movie-dune-2",
            Name = "Dune: Part Two",
            Type = "Movie",
            ProductionYear = 2024,
            BackdropImageTags = ["tag1"]
        };

        store.Save(item);
        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("movie-dune-2", loaded!.Id);
        Assert.Equal("Dune: Part Two", loaded.Name);
        Assert.Equal("Movie", loaded.Type);
        Assert.Equal(2024, loaded.ProductionYear);

        // Corrupt JSON returns null instead of throwing
        File.WriteAllText(storePath, "{ invalid-json");
        Assert.Null(store.Load());

        // Missing Id/Name payload returns null
        File.WriteAllText(storePath, "{\"Id\":\"\",\"Name\":\"NoId\"}");
        Assert.Null(store.Load());

        store.Save(item);
        store.Clear();
        Assert.False(File.Exists(storePath));
    }

    [Fact]
    public async Task SlideshowAndLaunchCoordinator_LaunchPersistedItemEvenAfterSlideshowStops()
    {
        Directory.CreateDirectory(_tempDir);
        var logger = new NullLoggingService();
        var settingsService = new SettingsService(logger, Path.Combine(_tempDir, "settings.json"));
        settingsService.Load();

        var storePath = Path.Combine(_tempDir, "current-wallpaper-item.json");
        var store = new CurrentWallpaperItemStore(logger, storePath);
        var timer = new FakeTimer();
        var imageFile = Path.Combine(_tempDir, "backdrop.jpg");
        File.WriteAllBytes(imageFile, [0xFF, 0xD8, 0xFF, 0xE0]);

        var slideshow = new SlideshowService(
            new FakeImageService(imageFile),
            settingsService,
            timer,
            logger,
            store);

        slideshow.SetItems(
        [
            new MediaItem { Id = "item-alpha", Name = "Interstellar", Type = "Movie", ProductionYear = 2014, BackdropImageTags = ["b1"] }
        ]);

        await slideshow.StartAsync();

        // Simulate app restart / stopped slideshow where no live item exists in memory
        slideshow.Stop();
        Assert.Null(slideshow.CurrentState.CurrentItem);

        var launcher = new CapturingItemLauncher();
        var coldCoordinator = new CurrentItemLaunchCoordinator(store, launcher, logger);

        var launchResult = await coldCoordinator.LaunchCurrentItemAsync();

        Assert.True(launchResult.Succeeded);
        Assert.Single(launcher.OpenedItems);
        Assert.Equal("item-alpha", launcher.OpenedItems[0].Id);
        Assert.Equal("Interstellar", launcher.OpenedItems[0].Name);
    }

    [Fact]
    public async Task CurrentItemLaunchCoordinator_WhenNoItemAvailable_ReturnsUserFriendlyError()
    {
        Directory.CreateDirectory(_tempDir);
        var logger = new NullLoggingService();
        var store = new CurrentWallpaperItemStore(logger, Path.Combine(_tempDir, "empty.json"));
        var launcher = new CapturingItemLauncher();
        var coordinator = new CurrentItemLaunchCoordinator(store, launcher, logger);

        var result = await coordinator.LaunchCurrentItemAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(CurrentItemLaunchCoordinator.MissingItemErrorMessage, result.Message);
        Assert.Empty(launcher.OpenedItems);
    }

    [Fact]
    public async Task MainViewModel_CreateOrUpdateDesktopShortcut_PreventsDuplicatesAndSyncsSettings()
    {
        Directory.CreateDirectory(_tempDir);
        var logger = new NullLoggingService();
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var settingsService = new SettingsService(logger, settingsPath);
        settingsService.Load();

        var store = new CurrentWallpaperItemStore(logger, Path.Combine(_tempDir, "current-item.json"));
        var launcher = new CapturingItemLauncher();
        var slideshow = new FakeSlideshowService();
        var coordinator = new CurrentItemLaunchCoordinator(store, launcher, logger, slideshow);
        var shortcutService = new StubDesktopShortcutService();
        var overlay = new StubOverlayService();

        var mainVm = new MainViewModel(
            new StubAuthService(),
            new StubLibraryService(),
            new FakeImageService(Path.Combine(_tempDir, "img.jpg")),
            new CacheService(logger, Path.Combine(_tempDir, "cache")),
            slideshow,
            new StubWallpaperService(),
            launcher,
            overlay,
            settingsService,
            logger,
            shortcutService,
            coordinator);

        var settingsVm = new SettingsViewModel(
            new StubAuthService(),
            new StubLibraryService(),
            new CacheService(logger, Path.Combine(_tempDir, "cache2")),
            slideshow,
            new StubWallpaperService(),
            launcher,
            overlay,
            shortcutService,
            new StubStartupService(),
            settingsService,
            logger);

        mainVm.DesktopShortcutSynchronized += (_, _) => settingsVm.SyncDesktopShortcutStateFromSettings();

        // Execute from MainWindow button / DataGrid context menu
        mainVm.CreateOrUpdateDesktopShortcutCommand.Execute(null);
        await Task.Delay(20);

        Assert.True(settingsService.Current.ShowDesktopShortcutIcon);
        Assert.True(settingsVm.ShowDesktopShortcutIcon);
        Assert.Equal(DesktopIconMode.Both, settingsVm.SelectedDesktopIconMode);
        Assert.Equal(1, shortcutService.CreateCalls);

        // Execute again -> updates same shortcut without duplicates
        mainVm.CreateOrUpdateDesktopShortcutCommand.Execute(null);
        await Task.Delay(20);
        Assert.Equal(2, shortcutService.CreateCalls);
        Assert.Contains("تکراری", mainVm.LaunchStatus);

        // Failure case surfaces clear error
        shortcutService.ShouldFail = true;
        mainVm.CreateOrUpdateDesktopShortcutCommand.Execute(null);
        await Task.Delay(20);
        Assert.False(string.IsNullOrWhiteSpace(mainVm.ErrorMessage));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class CapturingItemLauncher : IItemLauncher
    {
        public List<MediaItem> OpenedItems { get; } = [];
        public Task<ItemLaunchResult> OpenItemAsync(MediaItem item, CancellationToken cancellationToken = default)
        {
            OpenedItems.Add(item);
            return Task.FromResult(new ItemLaunchResult(true, "Opened"));
        }
        public bool OpenValidatedBrowserUrl(string url) => true;
        public bool TryValidateCustomUrlTemplate(string? template, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
        public Uri BuildWebDetailsUri(Uri serverUri, string itemId, string? serverId) => new($"{serverUri}");
    }

    private sealed class StubDesktopShortcutService : IDesktopShortcutService
    {
        public int CreateCalls { get; private set; }
        public bool IsPresent { get; private set; }
        public bool ShouldFail { get; set; }
        public string ShortcutFilePath => @"C:\Desktop\باز کردن در Jellyfin.lnk";
        public bool IsShortcutPresent() => IsPresent;

        public DesktopShortcutUpdateResult CreateOrUpdateShortcut()
        {
            CreateCalls++;
            if (ShouldFail)
            {
                return new DesktopShortcutUpdateResult(false, IsPresent, ShortcutFilePath, "خطای دسترسی در ساخت میان‌بُر.");
            }

            var existed = IsPresent;
            IsPresent = true;
            return new DesktopShortcutUpdateResult(
                true,
                existed,
                ShortcutFilePath,
                existed
                    ? "میان‌بر استاندارد دسکتاپ بدون ایجاد نسخهٔ تکراری به‌روزرسانی شد."
                    : "میان‌بر استاندارد روی دسکتاپ ساخته شد.");
        }

        public bool SetShortcutEnabled(bool enabled)
        {
            IsPresent = enabled;
            return true;
        }
    }

    private sealed class FakeTimer : ISlideshowTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeImageService : IImageService
    {
        private readonly string _imagePath;
        public FakeImageService(string imagePath) => _imagePath = imagePath;
        public Task<BackdropResolveResult> ResolveBackdropAsync(MediaItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new BackdropResolveResult
            {
                Status = BackdropResolveStatus.Success,
                LocalFilePath = _imagePath,
                Message = "OK"
            });
    }

    private sealed class FakeSlideshowService : ISlideshowService
    {
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
        public void Stop() { }
    }

    private sealed class StubOverlayService : IOverlayService
    {
        public bool IsVisible { get; private set; } = true;
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void RefreshFromSettings() { }
        public void Close() => IsVisible = false;
    }

    private sealed class StubStartupService : IStartupService
    {
        public bool IsStartupEnabled() => false;
        public bool SetStartupEnabled(bool enabled, bool startMinimized) => true;
    }

    private sealed class StubAuthService : IAuthenticationService
    {
        public ConnectionSessionState CurrentSession { get; } = new();
        public Task<ServerPublicInfo> TestConnectionAsync(string serverUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConnectionSessionState> LoginWithApiKeyAsync(string serverUrl, string apiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public bool HasCapturedOriginalWallpaper => false;
        public Task<WallpaperApplyResult> ApplyWallpaperAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK", imagePath));
        public Task<WallpaperApplyResult> ReapplyCurrentWallpaperAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));
        public Task<WallpaperApplyResult> RestoreOriginalWallpaperAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));
        public string GetMonitorsSummary() => "Display 1";
    }
}
