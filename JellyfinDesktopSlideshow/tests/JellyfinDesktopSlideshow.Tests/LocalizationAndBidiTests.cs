using System.Globalization;
using System.IO;
using System.Windows;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;
using JellyfinDesktopSlideshow.Views;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class LocalizationAndBidiTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-loc-tests-{Guid.NewGuid():N}");

    [Fact]
    public void JsonLanguagePackProvider_LoadsEmbeddedPersianAndEnglishPacksWithCompleteKeyParity()
    {
        var provider = new JsonLanguagePackProvider(
            typeof(LocalizationService).Assembly,
            Path.Combine(_tempDir, "non-existent-languages"));

        var packs = provider.LoadLanguagePacks();

        var faPack = packs.FirstOrDefault(p => string.Equals(p.Definition.Code, "fa-IR", StringComparison.OrdinalIgnoreCase));
        var enPack = packs.FirstOrDefault(p => string.Equals(p.Definition.Code, "en-US", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(faPack);
        Assert.NotNull(enPack);
        Assert.True(faPack!.Definition.IsRightToLeft);
        Assert.False(enPack!.Definition.IsRightToLeft);

        var missingInEn = faPack.Strings.Keys.Except(enPack.Strings.Keys, StringComparer.Ordinal).ToList();
        var missingInFa = enPack.Strings.Keys.Except(faPack.Strings.Keys, StringComparer.Ordinal).ToList();

        Assert.Empty(missingInEn);
        Assert.Empty(missingInFa);
        Assert.All(faPack.Strings.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(enPack.Strings.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void JsonLanguagePackProvider_LoadsExternalLanguagePackAndSupportsFallbackChain()
    {
        var languagesDir = Path.Combine(_tempDir, "languages");
        Directory.CreateDirectory(languagesDir);

        File.WriteAllText(
            Path.Combine(languagesDir, "de-DE.json"),
            """
            {
              "code": "de-DE",
              "nativeName": "Deutsch",
              "isRightToLeft": false,
              "strings": {
                "App.Title": "Jellyfin Desktop-Diashow"
              }
            }
            """);

        var provider = new JsonLanguagePackProvider(typeof(LocalizationService).Assembly, languagesDir);
        var localization = new LocalizationService(provider, "de");

        Assert.Contains(localization.AvailableLanguages, l => l.Code == "de-DE" && !l.IsRightToLeft);
        Assert.Equal("de-DE", localization.CurrentLanguage.Code);
        Assert.Equal(FlowDirection.LeftToRight, localization.CurrentFlowDirection);

        // Defined in custom external pack
        Assert.Equal("Jellyfin Desktop-Diashow", localization.GetString("App.Title"));

        // Missing in external pack -> falls back to en-US without code changes
        Assert.Equal("Disconnected", localization.GetString("Status.Disconnected"));

        // Missing in all packs -> safe bracketed key sentinel
        Assert.Equal("[Missing.Custom.Key]", localization.GetString("Missing.Custom.Key"));
    }

    [Fact]
    public void LocalizationService_SwitchesBetweenRtlPersianAndLtrEnglishAndRaisesEvents()
    {
        var provider = new JsonLanguagePackProvider(
            typeof(LocalizationService).Assembly,
            Path.Combine(_tempDir, "no-ext"));
        var localization = new LocalizationService(provider, "fa-IR");

        Assert.Equal("fa-IR", localization.CurrentLanguage.Code);
        Assert.True(localization.IsRightToLeft);
        Assert.Equal(FlowDirection.RightToLeft, localization.CurrentFlowDirection);

        var languageChangedCount = 0;
        var changedProps = new List<string?>();
        localization.LanguageChanged += (_, _) => languageChangedCount++;
        localization.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        var switched = localization.SetLanguage("en-US");
        Assert.True(switched);
        Assert.Equal("en-US", localization.CurrentLanguage.Code);
        Assert.False(localization.IsRightToLeft);
        Assert.Equal(FlowDirection.LeftToRight, localization.CurrentFlowDirection);
        Assert.Equal(1, languageChangedCount);
        Assert.Contains("Item[]", changedProps);
        Assert.Contains(nameof(ILocalizationService.CurrentFlowDirection), changedProps);

        // Same language again is a no-op
        Assert.False(localization.SetLanguage("en"));
        Assert.Equal(1, languageChangedCount);

        // Switching back to Persian restores RTL
        Assert.True(localization.SetLanguage("fa"));
        Assert.True(localization.IsRightToLeft);
        Assert.Equal(FlowDirection.RightToLeft, localization.CurrentFlowDirection);
        Assert.Equal(2, languageChangedCount);
    }

    [Fact]
    public void BidiTextFormatterAndConverter_HandleMixedPersianLatinTitlesAndServerDetails()
    {
        var latinTitle = BidiTextFormatter.FormatTitle("Andor", 2022);
        Assert.StartsWith(BidiTextFormatter.LeftToRightIsolate.ToString(), latinTitle);
        Assert.EndsWith(BidiTextFormatter.PopDirectionalIsolate.ToString(), latinTitle);
        Assert.Contains("Andor (2022)", latinTitle);

        var persianTitle = BidiTextFormatter.FormatTitle("جدایی نادر از سیمین", 2011);
        Assert.StartsWith(BidiTextFormatter.RightToLeftIsolate.ToString(), persianTitle);
        Assert.EndsWith(BidiTextFormatter.PopDirectionalIsolate.ToString(), persianTitle);
        Assert.Contains("جدایی نادر از سیمین (2011)", persianTitle);

        var serverDetails = BidiTextFormatter.FormatServerDetails("Home-NAS", "10.10.3", "Version");
        Assert.Contains($"{BidiTextFormatter.LeftToRightIsolate}Home-NAS{BidiTextFormatter.PopDirectionalIsolate}", serverDetails);
        Assert.Contains($"Version {BidiTextFormatter.LeftToRightIsolate}10.10.3{BidiTextFormatter.PopDirectionalIsolate}", serverDetails);

        Assert.Equal(FlowDirection.RightToLeft, BidiTextFormatter.DetectFlowDirection("سریال جدید (2024)", FlowDirection.LeftToRight));
        Assert.Equal(FlowDirection.LeftToRight, BidiTextFormatter.DetectFlowDirection("Blade Runner 2049", FlowDirection.RightToLeft));
        Assert.Equal(FlowDirection.RightToLeft, BidiTextFormatter.DetectFlowDirection("1999", FlowDirection.RightToLeft));

        var previousCode = AppLocalization.Current.CurrentLanguage.Code;
        try
        {
            AppLocalization.Current.SetLanguage("fa-IR");
            var converter = new DynamicTextFlowDirectionConverter();
            Assert.Equal(
                FlowDirection.LeftToRight,
                converter.Convert("The Last of Us", typeof(FlowDirection), null, CultureInfo.InvariantCulture));
            Assert.Equal(
                FlowDirection.RightToLeft,
                converter.Convert("قصه‌های مجید", typeof(FlowDirection), null, CultureInfo.InvariantCulture));
            Assert.Equal(
                FlowDirection.RightToLeft,
                converter.Convert("2025", typeof(FlowDirection), null, CultureInfo.InvariantCulture));
        }
        finally
        {
            AppLocalization.Current.SetLanguage(previousCode);
        }
    }

    [Fact]
    public void MainViewModelAndSettingsViewModel_SwitchLanguagePersistingSettingAndRefreshingLocalizedText()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var logger = new NullLoggingService();
        var settingsService = new SettingsService(logger, settingsPath);
        settingsService.Load();

        var provider = new JsonLanguagePackProvider(
            typeof(LocalizationService).Assembly,
            Path.Combine(_tempDir, "no-ext"));
        var localization = new LocalizationService(provider, settingsService.Current.Language);

        var auth = new StubAuthService();
        var libraries = new StubLibraryService();
        var cache = new CacheService(logger, Path.Combine(_tempDir, "cache"));
        var slideshow = new StubSlideshowService();
        var wallpaper = new StubWallpaperService();
        var launcher = new StubItemLauncher();
        var overlay = new StubOverlayService();
        var shortcut = new StubDesktopShortcutService();
        var startup = new StubStartupService();

        var mainVm = new MainViewModel(
            auth,
            libraries,
            cacheService: cache,
            imageService: new StubImageService(),
            slideshowService: slideshow,
            wallpaperService: wallpaper,
            itemLauncher: launcher,
            overlayService: overlay,
            settingsService: settingsService,
            logger: logger,
            desktopShortcutService: shortcut,
            launchCoordinator: null,
            localization: localization);

        var settingsVm = new SettingsViewModel(
            auth,
            libraries,
            cache,
            slideshow,
            wallpaper,
            launcher,
            overlay,
            shortcut,
            startup,
            settingsService,
            logger,
            cleanupService: null,
            localization: localization);

        Assert.Equal("fa-IR", mainVm.SelectedLanguageCode);
        Assert.Equal("fa-IR", settingsVm.SelectedLanguageCode);
        Assert.Equal(localization.GetString("Status.Disconnected"), mainVm.ConnectionStatus);
        Assert.Equal(localization.GetString("Option.WallpaperFit.Fill"), settingsVm.WallpaperFitModes[0].Value);
        Assert.Equal("کلید API (روش اصلی و پیشنهادی)", settingsVm.AuthMethodOptions[0].Value);
        Assert.Equal("کلید API (اصلی)", mainVm.AuthMethodOptions[0].Value);

        var mainSynced = 0;
        mainVm.LanguageSynchronized += (_, _) => mainSynced++;

        settingsVm.SelectedLanguageCode = "en-US";

        Assert.Equal("en-US", settingsService.Current.Language);
        Assert.Equal("en-US", mainVm.SelectedLanguageCode);
        Assert.False(localization.IsRightToLeft);
        Assert.Equal("Disconnected", mainVm.ConnectionStatus);
        Assert.Equal("Disconnected", settingsVm.ConnectionStatus);
        Assert.Equal("Fill", settingsVm.WallpaperFitModes[0].Value);
        Assert.Equal(
            ["Fill", "Fit", "Stretch", "Center", "Span (Multi-Monitor)"],
            settingsVm.WallpaperFitModes.Select(m => m.Value).ToArray());
        Assert.Equal("API Key (Primary & Recommended)", settingsVm.AuthMethodOptions[0].Value);
        Assert.Equal("API Key (Primary)", mainVm.AuthMethodOptions[0].Value);

        // Switching from MainViewModel propagates back to SettingsViewModel & persistence
        mainVm.SelectedLanguageCode = "fa-IR";
        Assert.Equal(1, mainSynced);
        Assert.Equal("fa-IR", settingsVm.SelectedLanguageCode);
        Assert.True(localization.IsRightToLeft);
        Assert.Equal("قطع", mainVm.ConnectionStatus);
        Assert.Equal("پر کردن صفحه (Fill)", settingsVm.WallpaperFitModes[0].Value);
        Assert.Equal(
            ["پر کردن صفحه (Fill)", "متناسب (Fit)", "کشیده (Stretch)", "مرکز (Center)", "گسترده چند مانیتور (Span)"],
            settingsVm.WallpaperFitModes.Select(m => m.Value).ToArray());
        Assert.Equal("کلید API (روش اصلی و پیشنهادی)", settingsVm.AuthMethodOptions[0].Value);
        Assert.Equal("کلید API (اصلی)", mainVm.AuthMethodOptions[0].Value);

        var reloaded = new SettingsService(logger, settingsPath).Load();
        Assert.Equal("fa-IR", reloaded.Language);
    }

    [Fact]
    public void SettingsViewModel_WallpaperFitModes_ReflectActiveLanguageAccurately()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings-fit.json");
        var logger = new NullLoggingService();
        var settingsService = new SettingsService(logger, settingsPath);
        settingsService.Load();

        var provider = new JsonLanguagePackProvider(
            typeof(LocalizationService).Assembly,
            Path.Combine(_tempDir, "no-ext-fit"));
        var localization = new LocalizationService(provider, "fa-IR");

        var settingsVm = new SettingsViewModel(
            new StubAuthService(),
            new StubLibraryService(),
            new CacheService(logger, Path.Combine(_tempDir, "cache-fit")),
            new StubSlideshowService(),
            new StubWallpaperService(),
            new StubItemLauncher(),
            new StubOverlayService(),
            new StubDesktopShortcutService(),
            new StubStartupService(),
            settingsService,
            logger,
            cleanupService: null,
            localization: localization);

        // Persian initial options
        Assert.Equal(
            [
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Fill, "پر کردن صفحه (Fill)"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Fit, "متناسب (Fit)"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Stretch, "کشیده (Stretch)"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Center, "مرکز (Center)"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Span, "گسترده چند مانیتور (Span)")
            ],
            settingsVm.WallpaperFitModes);

        // Switch to English
        settingsVm.SelectedLanguageCode = "en-US";

        Assert.Equal(
            [
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Fill, "Fill"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Fit, "Fit"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Stretch, "Stretch"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Center, "Center"),
                new KeyValuePair<WallpaperFitMode, string>(WallpaperFitMode.Span, "Span (Multi-Monitor)")
            ],
            settingsVm.WallpaperFitModes);
    }

    [Fact]
    public void ViewModels_AuthMethodOptions_ReflectActiveLanguageAccurately()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings-auth.json");
        var logger = new NullLoggingService();
        var settingsService = new SettingsService(logger, settingsPath);
        settingsService.Load();

        var provider = new JsonLanguagePackProvider(
            typeof(LocalizationService).Assembly,
            Path.Combine(_tempDir, "no-ext-auth"));
        var localization = new LocalizationService(provider, "fa-IR");

        var settingsVm = new SettingsViewModel(
            new StubAuthService(),
            new StubLibraryService(),
            new CacheService(logger, Path.Combine(_tempDir, "cache-auth")),
            new StubSlideshowService(),
            new StubWallpaperService(),
            new StubItemLauncher(),
            new StubOverlayService(),
            new StubDesktopShortcutService(),
            new StubStartupService(),
            settingsService,
            logger,
            cleanupService: null,
            localization: localization);

        var mainVm = new MainViewModel(
            new StubAuthService(),
            new StubLibraryService(),
            cacheService: new CacheService(logger, Path.Combine(_tempDir, "cache-main-auth")),
            imageService: new StubImageService(),
            slideshowService: new StubSlideshowService(),
            wallpaperService: new StubWallpaperService(),
            itemLauncher: new StubItemLauncher(),
            overlayService: new StubOverlayService(),
            settingsService: settingsService,
            logger: logger,
            desktopShortcutService: new StubDesktopShortcutService(),
            launchCoordinator: null,
            localization: localization);

        // Persian initial options
        Assert.Equal(
            [
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.ApiKey, "کلید API (روش اصلی و پیشنهادی)"),
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.UsernamePassword, "نام کاربری و رمز")
            ],
            settingsVm.AuthMethodOptions);

        Assert.Equal(
            [
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.ApiKey, "کلید API (اصلی)"),
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.UsernamePassword, "نام کاربری و رمز عبور")
            ],
            mainVm.AuthMethodOptions);

        // Switch to English
        settingsVm.SelectedLanguageCode = "en-US";

        Assert.Equal(
            [
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.ApiKey, "API Key (Primary & Recommended)"),
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.UsernamePassword, "Username & Password")
            ],
            settingsVm.AuthMethodOptions);

        Assert.Equal(
            [
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.ApiKey, "API Key (Primary)"),
                new KeyValuePair<JellyfinAuthMethod, string>(JellyfinAuthMethod.UsernamePassword, "Username & Password")
            ],
            mainVm.AuthMethodOptions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class StubAuthService : IAuthenticationService
    {
        public ConnectionSessionState CurrentSession { get; } = new() { IsAuthenticated = false };
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

    private sealed class StubImageService : IImageService
    {
        public Task<BackdropResolveResult> ResolveBackdropAsync(MediaItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new BackdropResolveResult { Status = BackdropResolveStatus.SkippedNoBackdrop });
    }

    private sealed class StubSlideshowService : ISlideshowService
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

    private sealed class StubWallpaperService : IWallpaperService
    {
        public string? LastAppliedWallpaperPath { get; set; }
        public bool HasCapturedOriginalWallpaper { get; set; }
        public Task<WallpaperApplyResult> ApplyWallpaperAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK", imagePath));
        public Task<WallpaperApplyResult> ReapplyCurrentWallpaperAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));
        public Task<WallpaperApplyResult> RestoreOriginalWallpaperAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WallpaperApplyResult(true, "OK"));
        public string GetMonitorsSummary() => "Display 1: 1920x1080";
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

    private sealed class StubOverlayService : IOverlayService
    {
        public bool IsVisible { get; private set; } = true;
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void RefreshFromSettings() { }
        public void Close() => IsVisible = false;
    }

    private sealed class StubDesktopShortcutService : IDesktopShortcutService
    {
        public bool IsPresent { get; private set; }
        public string ShortcutFilePath => @"C:\Desktop\Open in Jellyfin.lnk";
        public bool IsShortcutPresent() => IsPresent;
        public DesktopShortcutUpdateResult CreateOrUpdateShortcut()
        {
            IsPresent = true;
            return new DesktopShortcutUpdateResult(true, false, ShortcutFilePath, "OK");
        }
        public bool SetShortcutEnabled(bool enabled)
        {
            IsPresent = enabled;
            return true;
        }
    }

    private sealed class StubStartupService : IStartupService
    {
        public bool IsStartupEnabled() => false;
        public bool SetStartupEnabled(bool enabled, bool startMinimized) => true;
    }
}
