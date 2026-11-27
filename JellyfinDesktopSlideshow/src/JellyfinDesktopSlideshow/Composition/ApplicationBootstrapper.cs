using System.Globalization;
using System.Net.Http;
using System.Windows;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;
using JellyfinDesktopSlideshow.Views;

namespace JellyfinDesktopSlideshow.Composition;

public sealed class ApplicationBootstrapper
{
    private const string OpenCurrentSignalName = @"Local\JellyfinDesktopSlideshow_OpenCurrent_v1";

    private readonly Application _application;
    private readonly AppDataPaths _paths;
    private readonly ILocalizationService _localization;

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
    private IAppCleanupService? _cleanupService;
    private MainViewModel? _mainViewModel;
    private SettingsViewModel? _settingsViewModel;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private EventWaitHandle? _openCurrentSignal;
    private RegisteredWaitHandle? _openCurrentWaitRegistration;
    private bool _isShuttingDown;

    public ApplicationBootstrapper(
        Application application,
        AppDataPaths? paths = null,
        ILocalizationService? localization = null)
    {
        _application = application;
        _paths = paths ?? AppDataPaths.Default;
        _localization = localization ?? new LocalizationService(
            new JsonLanguagePackProvider(externalLanguagesDirectory: AppDataPaths.DefaultExternalLanguagesDirectory));
    }

    public ILoggingService? Logger => _logger;
    public ILocalizationService Localization => _localization;

    public async Task StartAsync(StartupEventArgs e)
    {
        AppLocalization.Configure(_localization);

        var openCurrentRequested = e.Args.Any(a =>
            string.Equals(a, DesktopShortcutService.OpenCurrentArgument, StringComparison.OrdinalIgnoreCase));

        if (openCurrentRequested && TrySignalRunningInstanceToOpenCurrent())
        {
            _application.Shutdown(0);
            return;
        }

        _logger = new LoggingService(_paths.LogsDirectory);
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var settingsService = new SettingsService(_logger, _paths.SettingsFilePath);
        _settingsService = settingsService;
        var loadedSettings = settingsService.Load();

        _localization.SetLanguage(loadedSettings.Language);
        SyncProcessUiCulture();
        _localization.LanguageChanged += OnLanguageChanged;

        var secureStorage = new SecureStorageService();
        var jellyfinClient = new JellyfinClient(_httpClient, _logger, _localization);
        var authService = new AuthenticationService(jellyfinClient, settingsService, secureStorage, _logger, _localization);
        var externalLauncher = new WindowsExternalLauncher(_logger);
        var itemLauncher = new ItemLauncher(jellyfinClient, authService, settingsService, externalLauncher, _logger, _localization);
        _itemLauncher = itemLauncher;
        var currentItemStore = new CurrentWallpaperItemStore(_logger, _paths.CurrentWallpaperItemFilePath);

        if (openCurrentRequested)
        {
            await RunColdOpenCurrentAsync(authService, currentItemStore, itemLauncher);
            return;
        }

        RegisterOpenCurrentSignalListener();

        var libraryService = new LibraryService(jellyfinClient, authService, settingsService, _localization);
        var cacheService = new CacheService(_logger, _paths.BackdropCacheDirectory);
        var imageService = new ImageService(jellyfinClient, authService, cacheService, settingsService, _logger, _localization);
        var slideshowTimer = new WpfDispatcherTimerAdapter();
        var slideshowService = new SlideshowService(
            imageService,
            settingsService,
            slideshowTimer,
            _logger,
            currentItemStore,
            _localization);
        _slideshowService = slideshowService;

        var launchCoordinator = new CurrentItemLaunchCoordinator(
            currentItemStore,
            itemLauncher,
            _logger,
            slideshowService,
            _localization);
        _launchCoordinator = launchCoordinator;

        var wallpaperAdapter = new WindowsWallpaperPlatformAdapter();
        var wallpaperService = new WallpaperService(
            wallpaperAdapter,
            settingsService,
            _logger,
            paths: _paths,
            localization: _localization);
        _wallpaperService = wallpaperService;

        var desktopShortcutService = new DesktopShortcutService(
            new WindowsDesktopShortcutAdapter(),
            _logger,
            iconFilePath: _paths.DesktopShortcutIconFilePath,
            localization: _localization);
        _desktopShortcutService = desktopShortcutService;
        desktopShortcutService.SetShortcutEnabled(loadedSettings.ShowDesktopShortcutIcon);

        var overlayViewModel = new OverlayViewModel(
            slideshowService,
            itemLauncher,
            settingsService,
            _logger,
            desktopShortcutService,
            launchCoordinator,
            _localization);
        _overlayService = new OverlayService(overlayViewModel, settingsService, itemLauncher, _logger, _localization);
        _overlayService.StateUpdated += (_, _) =>
        {
            _settingsViewModel?.SyncDesktopShortcutStateFromSettings();
            if (_mainViewModel is not null)
            {
                _ = _mainViewModel.SyncFromSettingsWindowAsync();
            }
        };

        var startupService = new StartupService(new WindowsStartupRegistryAdapter(), _logger);
        _cleanupService = new AppCleanupService(
            _paths,
            cacheService,
            wallpaperService,
            slideshowService,
            authService,
            secureStorage,
            startupService,
            desktopShortcutService,
            settingsService,
            _logger,
            _overlayService,
            currentItemStore,
            _localization);

        _trayService = new TrayService(_logger, _localization);

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
            launchCoordinator,
            _localization);

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
            _logger,
            _cleanupService,
            _localization);

        WireViewModelAndTrayEvents();

        _slideshowService.StateChanged += OnSlideshowStateChangedForTray;
        _trayService.Initialize();

        _mainWindow = new MainWindow(_mainViewModel, settingsService);
        _application.MainWindow = _mainWindow;
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

    private void WireViewModelAndTrayEvents()
    {
        if (_mainViewModel is null || _settingsViewModel is null || _trayService is null)
        {
            return;
        }

        _mainViewModel.OpenSettingsRequested += (_, _) => ShowSettingsWindow();
        _mainViewModel.MinimizeToTrayRequested += (_, _) => _mainWindow?.Hide();
        _mainViewModel.ExitApplicationRequested += async (_, _) => await ShutdownApplicationAsync();
        _mainViewModel.DesktopShortcutSynchronized += (_, _) => _settingsViewModel.SyncDesktopShortcutStateFromSettings();
        _mainViewModel.LanguageSynchronized += (_, _) => _settingsViewModel.LoadFromCurrentSettings();
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
    }

    private async Task RunColdOpenCurrentAsync(
        AuthenticationService authService,
        CurrentWallpaperItemStore currentItemStore,
        ItemLauncher itemLauncher)
    {
        var coldCoordinator = new CurrentItemLaunchCoordinator(
            currentItemStore,
            itemLauncher,
            _logger!,
            localization: _localization);
        await authService.TryRestoreSessionAsync();
        var coldResult = await coldCoordinator.LaunchCurrentItemAsync();
        if (!coldResult.Succeeded)
        {
            LaunchResultDialogHelper.HandleFailure(
                coldResult,
                itemLauncher,
                _logger,
                _localization.GetString("App.DesktopShortcutSource"));
        }

        _httpClient?.Dispose();
        _httpClient = null;
        _application.Shutdown(coldResult.Succeeded ? 0 : 1);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        SyncProcessUiCulture();
        if (_slideshowService is not null)
        {
            UpdateTrayTooltipFromState(_slideshowService.CurrentState);
        }
    }

    private void SyncProcessUiCulture()
    {
        try
        {
            var culture = _localization.CurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch
        {
            // Keep running cleanly if platform culture assignment fails
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
        => UpdateTrayTooltipFromState(state);

    private void UpdateTrayTooltipFromState(SlideshowState state)
    {
        if (_trayService is null)
        {
            return;
        }

        var tooltip = state.CurrentItem is not null
            ? _localization.Format("Tray.ToolTip.ItemFormat", state.CurrentItem.Name)
            : _localization.GetString("Tray.ToolTip.Default");

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
        var title = _localization.GetString("App.Title");
        var options = AppLocalization.GetMessageBoxOptions();

        if (result.Succeeded)
        {
            var settings = _settingsService.Current;
            settings.ShowDesktopShortcutIcon = true;
            _settingsService.Save(settings);
            _settingsViewModel?.SyncDesktopShortcutStateFromSettings();
            MessageBox.Show(
                result.Message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                MessageBoxResult.OK,
                options);
        }
        else
        {
            MessageBox.Show(
                result.Message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK,
                options);
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
            _localization.LanguageChanged -= OnLanguageChanged;

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
            _application.Shutdown();
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
                        _ = _application.Dispatcher.InvokeAsync(HandleExternalOpenCurrentRequestSafeAsync);
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

        LaunchResultDialogHelper.HandleFailure(
            result,
            _itemLauncher,
            _logger,
            _localization.GetString("App.DesktopShortcutSource"));
    }
}
