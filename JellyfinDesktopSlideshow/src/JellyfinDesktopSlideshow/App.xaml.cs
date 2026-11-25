using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;
using JellyfinDesktopSlideshow.Views;

namespace JellyfinDesktopSlideshow;

public partial class App : Application
{
    private HttpClient? _httpClient;
    private ILoggingService? _logger;
    private ISettingsService? _settingsService;
    private ISlideshowService? _slideshowService;
    private IWallpaperService? _wallpaperService;
    private IOverlayService? _overlayService;
    private ITrayService? _trayService;
    private MainViewModel? _mainViewModel;
    private SettingsViewModel? _settingsViewModel;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _logger = new LoggingService();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var settingsService = new SettingsService(_logger);
        _settingsService = settingsService;
        var loadedSettings = settingsService.Load();

        var secureStorage = new SecureStorageService();
        var jellyfinClient = new JellyfinClient(_httpClient, _logger);
        var authService = new AuthenticationService(jellyfinClient, settingsService, secureStorage, _logger);
        var libraryService = new LibraryService(jellyfinClient, authService, settingsService);
        var cacheService = new CacheService(_logger);
        var imageService = new ImageService(jellyfinClient, authService, cacheService, settingsService, _logger);
        var slideshowTimer = new WpfDispatcherTimerAdapter();
        var slideshowService = new SlideshowService(imageService, settingsService, slideshowTimer, _logger);
        _slideshowService = slideshowService;

        var wallpaperAdapter = new WindowsWallpaperPlatformAdapter();
        var wallpaperService = new WallpaperService(wallpaperAdapter, settingsService, _logger);
        _wallpaperService = wallpaperService;

        var externalLauncher = new WindowsExternalLauncher(_logger);
        var itemLauncher = new ItemLauncher(jellyfinClient, authService, settingsService, externalLauncher, _logger);
        var overlayViewModel = new OverlayViewModel(slideshowService, itemLauncher, settingsService, _logger);
        _overlayService = new OverlayService(overlayViewModel, settingsService, itemLauncher, _logger);

        var startupService = new StartupService(new WindowsStartupRegistryAdapter(), _logger);
        _trayService = new TrayService(_logger);

        _mainViewModel = new MainViewModel(
            authService,
            libraryService,
            imageService,
            cacheService,
            slideshowService,
            wallpaperService,
            itemLauncher,
            _overlayService,
            settingsService,
            _logger);

        _settingsViewModel = new SettingsViewModel(
            authService,
            libraryService,
            cacheService,
            slideshowService,
            wallpaperService,
            itemLauncher,
            _overlayService,
            startupService,
            settingsService,
            _logger);

        _mainViewModel.OpenSettingsRequested += (_, _) => ShowSettingsWindow();
        _mainViewModel.MinimizeToTrayRequested += (_, _) => _mainWindow?.Hide();
        _mainViewModel.ExitApplicationRequested += async (_, _) => await ShutdownApplicationAsync();
        _settingsViewModel.StateSynchronized += async (_, _) =>
        {
            if (_mainViewModel is not null)
            {
                await _mainViewModel.SyncFromSettingsWindowAsync();
            }
        };

        _trayService.ShowMainWindowRequested += (_, _) => ShowMainWindow();
        _trayService.OpenSettingsRequested += (_, _) => ShowSettingsWindow();
        _trayService.TogglePauseRequested += (_, _) => _slideshowService?.TogglePause();
        _trayService.NextSlideRequested += async (_, _) =>
        {
            if (_slideshowService is not null)
            {
                await _slideshowService.NextAsync();
            }
        };
        _trayService.OpenCurrentItemRequested += async (_, _) =>
        {
            if (_mainViewModel is not null)
            {
                await _mainViewModel.OpenCurrentItemFromExternalAsync();
            }
        };
        _trayService.ExitApplicationRequested += async (_, _) => await ShutdownApplicationAsync();

        _slideshowService.StateChanged += OnSlideshowStateChangedForTray;
        _trayService.Initialize();

        _mainWindow = new MainWindow(_mainViewModel, settingsService);
        MainWindow = _mainWindow;
        _mainWindow.Closed += async (_, _) =>
        {
            if (!_isShuttingDown)
            {
                await ShutdownApplicationAsync();
            }
        };

        if (loadedSettings.ShowOverlayIcon)
        {
            _overlayService.Show();
        }

        var startMinimized = e.Args.Any(a => string.Equals(a, StartupService.MinimizedArgument, StringComparison.OrdinalIgnoreCase)) ||
                             loadedSettings.StartMinimizedToTray;

        await _mainViewModel.InitializeAsync();

        if (!startMinimized)
        {
            _mainWindow.Show();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsViewModel is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsViewModel);
        if (_mainWindow is { IsVisible: true })
        {
            _settingsWindow.Owner = _mainWindow;
        }

        _settingsWindow.Closed += async (_, _) =>
        {
            _settingsWindow = null;
            if (_mainViewModel is not null && !_isShuttingDown)
            {
                await _mainViewModel.SyncFromSettingsWindowAsync();
            }
        };

        _settingsWindow.Show();
    }

    private void OnSlideshowStateChangedForTray(object? sender, SlideshowState state)
    {
        if (_trayService is null)
        {
            return;
        }

        var tooltip = state.CurrentItem is not null
            ? $"Jellyfin Slideshow — {state.CurrentItem.Name}"
            : "Jellyfin Desktop Slideshow";

        _trayService.UpdateState(
            tooltip,
            state.IsPaused,
            state.IsRunning,
            state.CurrentItem is not null);
    }

    private async Task ShutdownApplicationAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        try
        {
            if (_slideshowService is not null)
            {
                _slideshowService.StateChanged -= OnSlideshowStateChangedForTray;
                _slideshowService.Stop();
            }

            if (_settingsService?.Current.RestoreWallpaperOnExit == true &&
                _wallpaperService is { HasCapturedOriginalWallpaper: true })
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _wallpaperService.RestoreOriginalWallpaperAsync(cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("خطا هنگام پاک‌سازی پیش از خروج از برنامه.", ex);
        }
        finally
        {
            _settingsWindow?.Close();
            _overlayService?.Close();
            _trayService?.Dispose();
            _mainWindow?.ForceClose();
            _httpClient?.Dispose();
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("خطای مدیریت‌نشده در ترد رابط کاربری.", e.Exception);
        MessageBox.Show(
            "خطای غیرمنتظره‌ای رخ داد؛ جزئیات در فایل لاگ ثبت شد و برنامه به کار خود ادامه می‌دهد.",
            "Jellyfin Desktop Slideshow",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Error("خطای بحرانی در AppDomain.", e.ExceptionObject as Exception);
    }
}
