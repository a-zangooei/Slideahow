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
    private const string OpenCurrentSignalName = @"Local\JellyfinDesktopSlideshow_OpenCurrent_v1";

    private HttpClient? _httpClient;
    private ILoggingService? _logger;
    private ISettingsService? _settingsService;
    private ISlideshowService? _slideshowService;
    private IWallpaperService? _wallpaperService;
    private OverlayService? _overlayService;
    private IDesktopShortcutService? _desktopShortcutService;
    private IItemLauncher? _itemLauncher;
    private ICurrentItemLaunchCoordinator? _launchCoordinator;
    private ITrayService? _trayService;
    private MainViewModel? _mainViewModel;
    private SettingsViewModel? _settingsViewModel;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private EventWaitHandle? _openCurrentSignal;
    private RegisteredWaitHandle? _openCurrentWaitRegistration;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var openCurrentRequested = e.Args.Any(a =>
            string.Equals(a, DesktopShortcutService.OpenCurrentArgument, StringComparison.OrdinalIgnoreCase));

        if (openCurrentRequested && TrySignalRunningInstanceToOpenCurrent())
        {
            Shutdown(0);
            return;
        }

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
        var externalLauncher = new WindowsExternalLauncher(_logger);
        var itemLauncher = new ItemLauncher(jellyfinClient, authService, settingsService, externalLauncher, _logger);
        _itemLauncher = itemLauncher;
        var currentItemStore = new CurrentWallpaperItemStore(_logger);

        if (openCurrentRequested)
        {
            var coldCoordinator = new CurrentItemLaunchCoordinator(currentItemStore, itemLauncher, _logger);
            await authService.TryRestoreSessionAsync();
            var coldResult = await coldCoordinator.LaunchCurrentItemAsync();
            if (!coldResult.Succeeded)
            {
                LaunchResultDialogHelper.HandleFailure(coldResult, itemLauncher, _logger, "میان‌بر دسکتاپ");
            }

            _httpClient.Dispose();
            _httpClient = null;
            Shutdown(coldResult.Succeeded ? 0 : 1);
            return;
        }

        RegisterOpenCurrentSignalListener();

        var libraryService = new LibraryService(jellyfinClient, authService, settingsService);
        var cacheService = new CacheService(_logger);
        var imageService = new ImageService(jellyfinClient, authService, cacheService, settingsService, _logger);
        var slideshowTimer = new WpfDispatcherTimerAdapter();
        var slideshowService = new SlideshowService(imageService, settingsService, slideshowTimer, _logger, currentItemStore);
        _slideshowService = slideshowService;

        var launchCoordinator = new CurrentItemLaunchCoordinator(currentItemStore, itemLauncher, _logger, slideshowService);
        _launchCoordinator = launchCoordinator;

        var wallpaperAdapter = new WindowsWallpaperPlatformAdapter();
        var wallpaperService = new WallpaperService(wallpaperAdapter, settingsService, _logger);
        _wallpaperService = wallpaperService;

        var desktopShortcutService = new DesktopShortcutService(new WindowsDesktopShortcutAdapter(), _logger);
        _desktopShortcutService = desktopShortcutService;
        desktopShortcutService.SetShortcutEnabled(loadedSettings.ShowDesktopShortcutIcon);

        var overlayViewModel = new OverlayViewModel(
            slideshowService,
            itemLauncher,
            settingsService,
            _logger,
            desktopShortcutService,
            launchCoordinator);
        _overlayService = new OverlayService(overlayViewModel, settingsService, itemLauncher, _logger);
        _overlayService.StateUpdated += (_, _) =>
        {
            _settingsViewModel?.SyncDesktopShortcutStateFromSettings();
            if (_mainViewModel is not null)
            {
                _ = _mainViewModel.SyncFromSettingsWindowAsync();
            }
        };

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
            _logger,
            desktopShortcutService,
            launchCoordinator);

        _settingsViewModel = new SettingsViewModel(
            authService,
            libraryService,
            cacheService,
            slideshowService,
            wallpaperService,
            itemLauncher,
            _overlayService,
            desktopShortcutService,
            startupService,
            settingsService,
            _logger);

        _mainViewModel.OpenSettingsRequested += (_, _) => ShowSettingsWindow();
        _mainViewModel.MinimizeToTrayRequested += (_, _) => _mainWindow?.Hide();
        _mainViewModel.ExitApplicationRequested += async (_, _) => await ShutdownApplicationAsync();
        _mainViewModel.DesktopShortcutSynchronized += (_, _) => _settingsViewModel?.SyncDesktopShortcutStateFromSettings();
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
        _trayService.OpenCurrentItemRequested += async (_, _) => await HandleExternalOpenCurrentRequestAsync();
        _trayService.CreateDesktopShortcutRequested += (_, _) => HandleCreateDesktopShortcutFromTray();
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

        var hasResolvableItem = state.CurrentItem is not null || _launchCoordinator?.ResolveCurrentItem() is not null;
        _trayService.UpdateState(
            tooltip,
            state.IsPaused,
            state.IsRunning,
            hasResolvableItem);
    }

    private void HandleCreateDesktopShortcutFromTray()
    {
        if (_desktopShortcutService is null || _settingsService is null)
        {
            return;
        }

        var result = _desktopShortcutService.CreateOrUpdateShortcut();
        if (result.Succeeded)
        {
            var settings = _settingsService.Current;
            settings.ShowDesktopShortcutIcon = true;
            _settingsService.Save(settings);
            _settingsViewModel?.SyncDesktopShortcutStateFromSettings();
            MessageBox.Show(
                result.Message,
                "Jellyfin Desktop Slideshow",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                result.Message,
                "Jellyfin Desktop Slideshow",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
            _openCurrentWaitRegistration?.Unregister(null);
            _openCurrentWaitRegistration = null;
            _openCurrentSignal?.Dispose();
            _openCurrentSignal = null;
            _settingsWindow?.Close();
            _overlayService?.Close();
            _trayService?.Dispose();
            _mainWindow?.ForceClose();
            _httpClient?.Dispose();
            Shutdown();
        }
    }

    private static bool TrySignalRunningInstanceToOpenCurrent()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(OpenCurrentSignalName, out var existingSignal))
            {
                using (existingSignal)
                {
                    existingSignal.Set();
                }

                return true;
            }
        }
        catch
        {
            // Fall back to direct launch if IPC event cannot be opened.
        }

        return false;
    }

    private void RegisterOpenCurrentSignalListener()
    {
        try
        {
            _openCurrentSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenCurrentSignalName);
            _openCurrentWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
                _openCurrentSignal,
                (_, timedOut) =>
                {
                    if (!timedOut && !_isShuttingDown)
                    {
                        _ = Dispatcher.InvokeAsync(HandleExternalOpenCurrentRequestSafeAsync);
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception ex)
        {
            _logger?.Warning($"ثبت شنوندهٔ میان‌بُر دسکتاپ با خطا مواجه شد: {ex.Message}");
        }
    }

    private async Task HandleExternalOpenCurrentRequestSafeAsync()
    {
        try
        {
            await HandleExternalOpenCurrentRequestAsync();
        }
        catch (Exception ex)
        {
            _logger?.Error("خطای غیرمنتظره هنگام اجرای فرمان باز کردن از آیکون دسکتاپ.", ex);
        }
    }

    private async Task HandleExternalOpenCurrentRequestAsync()
    {
        if (_isShuttingDown || _launchCoordinator is null || _itemLauncher is null)
        {
            return;
        }

        var result = await _launchCoordinator.LaunchCurrentItemAsync(_mainViewModel?.SelectedMediaItem);
        if (result.Succeeded)
        {
            return;
        }

        LaunchResultDialogHelper.HandleFailure(result, _itemLauncher, _logger, "میان‌بر دسکتاپ");
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
