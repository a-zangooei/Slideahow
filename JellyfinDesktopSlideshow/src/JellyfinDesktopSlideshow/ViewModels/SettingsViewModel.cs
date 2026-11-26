using System.Collections.ObjectModel;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;

namespace JellyfinDesktopSlideshow.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILibraryService _libraryService;
    private readonly ICacheService _cacheService;
    private readonly ISlideshowService _slideshowService;
    private readonly IWallpaperService _wallpaperService;
    private readonly IItemLauncher _itemLauncher;
    private readonly IOverlayService _overlayService;
    private readonly IDesktopShortcutService _desktopShortcutService;
    private readonly IStartupService _startupService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;

    private string _serverUrl = string.Empty;
    private string _username = string.Empty;
    private string _connectionStatus = "متصل نیست";
    private string _serverDetails = string.Empty;
    private string _securityWarning = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private string _itemsSummary = string.Empty;
    private string _cacheSummary = string.Empty;
    private string _monitorsSummary = string.Empty;
    private string _fallbackImagePath = string.Empty;
    private string _customUrlTemplate = string.Empty;
    private string _customUrlValidationMessage = string.Empty;

    private bool _isBusy;
    private bool _isAuthenticated;
    private bool _includeMovies = true;
    private bool _includeSeries = true;

    private int _slideshowIntervalSeconds = 300;
    private bool _isRandomOrder = true;
    private int _nonRepeatHistoryCount = 10;
    private OfflineBehaviorMode _selectedOfflineBehavior = OfflineBehaviorMode.UseCacheAndRetry;
    private bool _applyToDesktopWallpaper = true;
    private WallpaperFitMode _selectedWallpaperFitMode = WallpaperFitMode.Fill;
    private bool _restoreWallpaperOnExit;

    private bool _showOverlayIcon = true;
    private bool _showDesktopShortcutIcon;
    private double _overlayLeft = 32;
    private double _overlayTop = 32;
    private double _overlaySize = 44;
    private double _overlayOpacity = 0.85;

    private PlaybackTargetType _selectedPlaybackTargetType = PlaybackTargetType.DefaultBrowser;

    private int _maxCacheSizeMb = 512;
    private int _maxCacheAgeDays = 14;
    private int _backdropIndexPreference;
    private bool _randomBackdropPerItem;

    private bool _launchOnWindowsStartup;
    private bool _startMinimizedToTray;
    private bool _minimizeToTrayOnClose = true;

    public ObservableCollection<LibrarySelectionItem> Libraries { get; } = [];

    public IReadOnlyList<KeyValuePair<WallpaperFitMode, string>> WallpaperFitModes { get; } =
    [
        new(WallpaperFitMode.Fill, "پر کردن صفحه (Fill)"),
        new(WallpaperFitMode.Fit, "متناسب (Fit)"),
        new(WallpaperFitMode.Stretch, "کشیده (Stretch)"),
        new(WallpaperFitMode.Center, "مرکز (Center)"),
        new(WallpaperFitMode.Span, "گسترده چند مانیتور (Span)")
    ];

    public IReadOnlyList<KeyValuePair<OfflineBehaviorMode, string>> OfflineBehaviorOptions { get; } =
    [
        new(OfflineBehaviorMode.UseCacheAndRetry, "استفاده از کش و تلاش مجدد خودکار"),
        new(OfflineBehaviorMode.KeepCurrentWallpaper, "حفظ والپیپر فعلی بدون خطا"),
        new(OfflineBehaviorMode.PauseSlideshow, "توقف (مکث) اسلایدشو هنگام قطع اتصال")
    ];

    public IReadOnlyList<KeyValuePair<PlaybackTargetType, string>> PlaybackTargetOptions { get; } =
    [
        new(PlaybackTargetType.DefaultBrowser, "مرورگر پیش‌فرض"),
        new(PlaybackTargetType.JellyfinDesktopClient, "برنامهٔ Jellyfin دسکتاپ"),
        new(PlaybackTargetType.CustomUrlTemplate, "الگوی URL سفارشی")
    ];

    public IReadOnlyList<KeyValuePair<DesktopIconMode, string>> DesktopIconModes { get; } =
    [
        new(DesktopIconMode.FloatingOverlay, "آیکون شناور روی صفحه"),
        new(DesktopIconMode.DesktopShortcut, "آیکون استاندارد روی دسکتاپ (مانند سایر آیکون‌ها)"),
        new(DesktopIconMode.Both, "هر دو حالت (آیکون شناور + آیکون دسکتاپ)"),
        new(DesktopIconMode.Disabled, "غیرفعال")
    ];

    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public AsyncRelayCommand LoadLibrariesCommand { get; }
    public AsyncRelayCommand ApplyMediaSelectionCommand { get; }
    public AsyncRelayCommand ClearFallbackImageCommand { get; }
    public AsyncRelayCommand ClearCacheCommand { get; }
    public AsyncRelayCommand PruneCacheCommand { get; }

    public Task PendingWallpaperOperationTask { get; private set; } = Task.CompletedTask;

    public event EventHandler? StateSynchronized;

    public SettingsViewModel(
        IAuthenticationService authService,
        ILibraryService libraryService,
        ICacheService cacheService,
        ISlideshowService slideshowService,
        IWallpaperService wallpaperService,
        IItemLauncher itemLauncher,
        IOverlayService overlayService,
        IDesktopShortcutService desktopShortcutService,
        IStartupService startupService,
        ISettingsService settingsService,
        ILoggingService logger)
    {
        _authService = authService;
        _libraryService = libraryService;
        _cacheService = cacheService;
        _slideshowService = slideshowService;
        _wallpaperService = wallpaperService;
        _itemLauncher = itemLauncher;
        _overlayService = overlayService;
        _desktopShortcutService = desktopShortcutService;
        _startupService = startupService;
        _settingsService = settingsService;
        _logger = logger;

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => !IsBusy && IsAuthenticated);
        LoadLibrariesCommand = new AsyncRelayCommand(LoadLibrariesAsync, () => !IsBusy && IsAuthenticated);
        ApplyMediaSelectionCommand = new AsyncRelayCommand(ApplyMediaSelectionAsync, () => !IsBusy && IsAuthenticated);
        ClearFallbackImageCommand = new AsyncRelayCommand(ClearFallbackImageAsync, () => !string.IsNullOrWhiteSpace(FallbackImagePath));
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !IsBusy);
        PruneCacheCommand = new AsyncRelayCommand(PruneCacheAsync, () => !IsBusy);

        LoadFromCurrentSettings();
    }

    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string ServerDetails
    {
        get => _serverDetails;
        private set => SetProperty(ref _serverDetails, value);
    }

    public string SecurityWarning
    {
        get => _securityWarning;
        private set => SetProperty(ref _securityWarning, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ItemsSummary
    {
        get => _itemsSummary;
        private set => SetProperty(ref _itemsSummary, value);
    }

    public string CacheSummary
    {
        get => _cacheSummary;
        private set => SetProperty(ref _cacheSummary, value);
    }

    public string MonitorsSummary
    {
        get => _monitorsSummary;
        private set => SetProperty(ref _monitorsSummary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (SetProperty(ref _isAuthenticated, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool IncludeMovies
    {
        get => _includeMovies;
        set => SetProperty(ref _includeMovies, value);
    }

    public bool IncludeSeries
    {
        get => _includeSeries;
        set => SetProperty(ref _includeSeries, value);
    }

    public int SlideshowIntervalSeconds
    {
        get => _slideshowIntervalSeconds;
        set
        {
            var clamped = Math.Clamp(value, 5, 86400);
            if (SetProperty(ref _slideshowIntervalSeconds, clamped))
            {
                SaveSetting(s => s.SlideshowIntervalSeconds = clamped);
                _slideshowService.UpdateConfiguration();
            }
        }
    }

    public bool IsRandomOrder
    {
        get => _isRandomOrder;
        set
        {
            if (SetProperty(ref _isRandomOrder, value))
            {
                SaveSetting(s => s.OrderMode = value ? SlideshowOrderMode.Random : SlideshowOrderMode.Sequential);
                _slideshowService.UpdateConfiguration();
            }
        }
    }

    public int NonRepeatHistoryCount
    {
        get => _nonRepeatHistoryCount;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _nonRepeatHistoryCount, clamped))
            {
                SaveSetting(s => s.NonRepeatHistoryCount = clamped);
            }
        }
    }

    public OfflineBehaviorMode SelectedOfflineBehavior
    {
        get => _selectedOfflineBehavior;
        set
        {
            if (SetProperty(ref _selectedOfflineBehavior, value))
            {
                SaveSetting(s => s.OfflineBehavior = value);
            }
        }
    }

    public bool ApplyToDesktopWallpaper
    {
        get => _applyToDesktopWallpaper;
        set
        {
            if (SetProperty(ref _applyToDesktopWallpaper, value))
            {
                SaveSetting(s => s.ApplyToDesktopWallpaper = value);
                if (!value)
                {
                    if (_wallpaperService.HasCapturedOriginalWallpaper)
                    {
                        PendingWallpaperOperationTask = RestoreWallpaperOnFeatureDisabledAsync();
                    }
                    else
                    {
                        StatusMessage = "اعمال خودکار والپیپر غیرفعال شد.";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(_slideshowService.CurrentState.CurrentImagePath))
                {
                    PendingWallpaperOperationTask = ApplyCurrentSlideOnFeatureEnabledAsync(
                        _slideshowService.CurrentState.CurrentImagePath);
                }
            }
        }
    }

    public WallpaperFitMode SelectedWallpaperFitMode
    {
        get => _selectedWallpaperFitMode;
        set
        {
            if (SetProperty(ref _selectedWallpaperFitMode, value))
            {
                SaveSetting(s => s.WallpaperFitMode = value);
                if (ApplyToDesktopWallpaper && !string.IsNullOrWhiteSpace(_wallpaperService.LastAppliedWallpaperPath))
                {
                    PendingWallpaperOperationTask = ReapplyCurrentWallpaperSafeAsync();
                }
            }
        }
    }

    public bool RestoreWallpaperOnExit
    {
        get => _restoreWallpaperOnExit;
        set
        {
            if (SetProperty(ref _restoreWallpaperOnExit, value))
            {
                SaveSetting(s => s.RestoreWallpaperOnExit = value);
            }
        }
    }

    public DesktopIconMode SelectedDesktopIconMode
    {
        get => (_showOverlayIcon, _showDesktopShortcutIcon) switch
        {
            (true, true) => DesktopIconMode.Both,
            (true, false) => DesktopIconMode.FloatingOverlay,
            (false, true) => DesktopIconMode.DesktopShortcut,
            _ => DesktopIconMode.Disabled
        };
        set
        {
            var (wantOverlay, wantShortcut) = value switch
            {
                DesktopIconMode.FloatingOverlay => (true, false),
                DesktopIconMode.DesktopShortcut => (false, true),
                DesktopIconMode.Both => (true, true),
                _ => (false, false)
            };

            if (wantOverlay == _showOverlayIcon && wantShortcut == _showDesktopShortcutIcon)
            {
                return;
            }

            if (wantShortcut != _showDesktopShortcutIcon)
            {
                var applied = _desktopShortcutService.SetShortcutEnabled(wantShortcut);
                if (!applied)
                {
                    ErrorMessage = "ایجاد یا حذف آیکون استاندارد دسکتاپ ناموفق بود.";
                    _showDesktopShortcutIcon = _desktopShortcutService.IsShortcutPresent();
                    OnPropertyChanged(nameof(ShowDesktopShortcutIcon));
                    OnPropertyChanged(nameof(SelectedDesktopIconMode));
                    return;
                }

                _showDesktopShortcutIcon = wantShortcut;
                OnPropertyChanged(nameof(ShowDesktopShortcutIcon));
            }

            var overlayChanged = wantOverlay != _showOverlayIcon;
            if (overlayChanged)
            {
                _showOverlayIcon = wantOverlay;
                if (wantOverlay)
                {
                    _overlayService.Show();
                }
                else
                {
                    _overlayService.Hide();
                }

                OnPropertyChanged(nameof(ShowOverlayIcon));
                OnPropertyChanged(nameof(IsFloatingOverlayEnabled));
            }

            SaveSetting(s =>
            {
                s.ShowOverlayIcon = _showOverlayIcon;
                s.ShowDesktopShortcutIcon = _showDesktopShortcutIcon;
            });

            ErrorMessage = string.Empty;
            StatusMessage = value switch
            {
                DesktopIconMode.FloatingOverlay => "حالت آیکون شناور روی صفحه فعال شد.",
                DesktopIconMode.DesktopShortcut => "آیکون استاندارد روی دسکتاپ («باز کردن در Jellyfin») ایجاد شد.",
                DesktopIconMode.Both => "هر دو حالت آیکون شناور و آیکون استاندارد دسکتاپ فعال شدند.",
                _ => "نمایش آیکون باز کردن Jellyfin غیرفعال شد."
            };

            OnPropertyChanged(nameof(SelectedDesktopIconMode));
            if (overlayChanged)
            {
                StateSynchronized?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsFloatingOverlayEnabled => ShowOverlayIcon;

    public bool ShowOverlayIcon
    {
        get => _showOverlayIcon;
        set
        {
            if (SetProperty(ref _showOverlayIcon, value))
            {
                SaveSetting(s => s.ShowOverlayIcon = value);
                if (value)
                {
                    _overlayService.Show();
                }
                else
                {
                    _overlayService.Hide();
                }

                OnPropertyChanged(nameof(IsFloatingOverlayEnabled));
                OnPropertyChanged(nameof(SelectedDesktopIconMode));
                StateSynchronized?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowDesktopShortcutIcon
    {
        get => _showDesktopShortcutIcon;
        set
        {
            if (_showDesktopShortcutIcon == value)
            {
                return;
            }

            var applied = _desktopShortcutService.SetShortcutEnabled(value);
            if (!applied)
            {
                ErrorMessage = "ایجاد یا حذف آیکون استاندارد دسکتاپ ناموفق بود.";
                _showDesktopShortcutIcon = _desktopShortcutService.IsShortcutPresent();
                OnPropertyChanged(nameof(ShowDesktopShortcutIcon));
                OnPropertyChanged(nameof(SelectedDesktopIconMode));
                return;
            }

            _showDesktopShortcutIcon = value;
            SaveSetting(s => s.ShowDesktopShortcutIcon = value);
            ErrorMessage = string.Empty;
            StatusMessage = value
                ? "آیکون استاندارد روی دسکتاپ («باز کردن در Jellyfin») ایجاد شد."
                : "آیکون استاندارد از دسکتاپ حذف شد.";
            OnPropertyChanged(nameof(ShowDesktopShortcutIcon));
            OnPropertyChanged(nameof(SelectedDesktopIconMode));
        }
    }

    public double OverlayLeft
    {
        get => _overlayLeft;
        set
        {
            if (SetProperty(ref _overlayLeft, value))
            {
                SaveSetting(s => s.OverlayLeft = value);
                _overlayService.RefreshFromSettings();
            }
        }
    }

    public double OverlayTop
    {
        get => _overlayTop;
        set
        {
            if (SetProperty(ref _overlayTop, value))
            {
                SaveSetting(s => s.OverlayTop = value);
                _overlayService.RefreshFromSettings();
            }
        }
    }

    public double OverlaySize
    {
        get => _overlaySize;
        set
        {
            var clamped = Math.Clamp(value, 24, 96);
            if (SetProperty(ref _overlaySize, clamped))
            {
                SaveSetting(s => s.OverlaySize = clamped);
                _overlayService.RefreshFromSettings();
            }
        }
    }

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0.2, 1.0);
            if (SetProperty(ref _overlayOpacity, clamped))
            {
                SaveSetting(s => s.OverlayOpacity = clamped);
                _overlayService.RefreshFromSettings();
            }
        }
    }

    public PlaybackTargetType SelectedPlaybackTargetType
    {
        get => _selectedPlaybackTargetType;
        set
        {
            if (SetProperty(ref _selectedPlaybackTargetType, value))
            {
                SaveSetting(s => s.PlaybackTarget.TargetType = value);
                OnPropertyChanged(nameof(IsCustomUrlTargetSelected));
                ValidateAndSaveCustomUrlTemplate();
            }
        }
    }

    public bool IsCustomUrlTargetSelected => SelectedPlaybackTargetType == PlaybackTargetType.CustomUrlTemplate;

    public string CustomUrlTemplate
    {
        get => _customUrlTemplate;
        set
        {
            if (SetProperty(ref _customUrlTemplate, value))
            {
                ValidateAndSaveCustomUrlTemplate();
            }
        }
    }

    public string CustomUrlValidationMessage
    {
        get => _customUrlValidationMessage;
        private set => SetProperty(ref _customUrlValidationMessage, value);
    }

    public int MaxCacheSizeMb
    {
        get => _maxCacheSizeMb;
        set
        {
            var clamped = Math.Clamp(value, 32, 8192);
            if (SetProperty(ref _maxCacheSizeMb, clamped))
            {
                SaveSetting(s => s.MaxCacheSizeMb = clamped);
            }
        }
    }

    public int MaxCacheAgeDays
    {
        get => _maxCacheAgeDays;
        set
        {
            var clamped = Math.Clamp(value, 1, 365);
            if (SetProperty(ref _maxCacheAgeDays, clamped))
            {
                SaveSetting(s => s.MaxCacheAgeDays = clamped);
            }
        }
    }

    public int BackdropIndexPreference
    {
        get => _backdropIndexPreference;
        set
        {
            var clamped = Math.Clamp(value, 0, 20);
            if (SetProperty(ref _backdropIndexPreference, clamped))
            {
                SaveSetting(s => s.BackdropIndexPreference = clamped);
            }
        }
    }

    public bool RandomBackdropPerItem
    {
        get => _randomBackdropPerItem;
        set
        {
            if (SetProperty(ref _randomBackdropPerItem, value))
            {
                SaveSetting(s => s.RandomBackdropPerItem = value);
            }
        }
    }

    public string FallbackImagePath
    {
        get => _fallbackImagePath;
        private set
        {
            if (SetProperty(ref _fallbackImagePath, value))
            {
                ClearFallbackImageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool LaunchOnWindowsStartup
    {
        get => _launchOnWindowsStartup;
        set
        {
            if (SetProperty(ref _launchOnWindowsStartup, value))
            {
                var applied = _startupService.SetStartupEnabled(value, StartMinimizedToTray);
                if (!applied)
                {
                    ErrorMessage = "تغییر وضعیت اجرای خودکار در رجیستری ویندوز ناموفق بود.";
                    _launchOnWindowsStartup = _startupService.IsStartupEnabled();
                    OnPropertyChanged(nameof(LaunchOnWindowsStartup));
                    return;
                }

                SaveSetting(s => s.LaunchOnWindowsStartup = value);
                StatusMessage = value
                    ? "اجرای خودکار هنگام ورود به ویندوز فعال شد."
                    : "اجرای خودکار غیرفعال شد.";
            }
        }
    }

    public bool StartMinimizedToTray
    {
        get => _startMinimizedToTray;
        set
        {
            if (SetProperty(ref _startMinimizedToTray, value))
            {
                SaveSetting(s => s.StartMinimizedToTray = value);
                if (LaunchOnWindowsStartup)
                {
                    _startupService.SetStartupEnabled(true, value);
                }
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (SetProperty(ref _minimizeToTrayOnClose, value))
            {
                SaveSetting(s => s.MinimizeToTrayOnClose = value);
            }
        }
    }

    public async Task InitializeAsync()
    {
        LoadFromCurrentSettings();
        RefreshSessionPresentation();
        RefreshCacheSummary();
        MonitorsSummary = _wallpaperService.GetMonitorsSummary();

        if (IsAuthenticated && Libraries.Count == 0)
        {
            await LoadLibrariesAsync();
        }
    }

    public void LoadFromCurrentSettings()
    {
        var s = _settingsService.Current;
        _serverUrl = s.ServerUrl;
        _username = s.Username ?? string.Empty;
        _includeMovies = s.IncludedItemTypes.Contains("Movie", StringComparer.OrdinalIgnoreCase);
        _includeSeries = s.IncludedItemTypes.Contains("Series", StringComparer.OrdinalIgnoreCase);
        _slideshowIntervalSeconds = s.SlideshowIntervalSeconds;
        _isRandomOrder = s.OrderMode == SlideshowOrderMode.Random;
        _nonRepeatHistoryCount = s.NonRepeatHistoryCount;
        _selectedOfflineBehavior = s.OfflineBehavior;
        _applyToDesktopWallpaper = s.ApplyToDesktopWallpaper;
        _selectedWallpaperFitMode = s.WallpaperFitMode;
        _restoreWallpaperOnExit = s.RestoreWallpaperOnExit;
        _showOverlayIcon = s.ShowOverlayIcon;
        _showDesktopShortcutIcon = s.ShowDesktopShortcutIcon;
        _overlayLeft = s.OverlayLeft;
        _overlayTop = s.OverlayTop;
        _overlaySize = s.OverlaySize;
        _overlayOpacity = s.OverlayOpacity;
        _selectedPlaybackTargetType = s.PlaybackTarget.TargetType;
        _customUrlTemplate = s.PlaybackTarget.CustomUrlTemplate ?? string.Empty;
        _maxCacheSizeMb = s.MaxCacheSizeMb;
        _maxCacheAgeDays = s.MaxCacheAgeDays;
        _backdropIndexPreference = s.BackdropIndexPreference;
        _randomBackdropPerItem = s.RandomBackdropPerItem;
        _fallbackImagePath = s.FallbackImagePath ?? string.Empty;
        _launchOnWindowsStartup = s.LaunchOnWindowsStartup || _startupService.IsStartupEnabled();
        _startMinimizedToTray = s.StartMinimizedToTray;
        _minimizeToTrayOnClose = s.MinimizeToTrayOnClose;

        OnPropertyChanged(string.Empty);
    }

    public void SyncDesktopShortcutStateFromSettings()
    {
        var s = _settingsService.Current;
        _showOverlayIcon = s.ShowOverlayIcon;
        _showDesktopShortcutIcon = s.ShowDesktopShortcutIcon;
        OnPropertyChanged(nameof(ShowOverlayIcon));
        OnPropertyChanged(nameof(ShowDesktopShortcutIcon));
        OnPropertyChanged(nameof(IsFloatingOverlayEnabled));
        OnPropertyChanged(nameof(SelectedDesktopIconMode));
    }

    public async Task<bool> LoginAsync(string password)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        try
        {
            await _authService.LoginAsync(ServerUrl, Username, password);
            RefreshSessionPresentation();
            await LoadLibrariesInternalAsync();
            await ApplyMediaSelectionInternalAsync();
            StatusMessage = "ورود با موفقیت انجام شد و فهرست رسانه‌ها دریافت گردید.";
            StateSynchronized?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (JellyfinApiException ex)
        {
            ErrorMessage = ex.Message;
            ConnectionStatus = "خطا در ورود";
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error("خطای غیرمنتظره هنگام ورود از پنجرهٔ تنظیمات.", ex);
            ErrorMessage = "خطای غیرمنتظره هنگام ورود رخ داد.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetFallbackImagePath(string path)
    {
        if (!ImageFormatValidator.IsValidImageFile(path))
        {
            ErrorMessage = "فایل انتخاب‌شده تصویر معتبری نیست (فرمت‌های مجاز: JPG, PNG, WebP, BMP).";
            return;
        }

        ErrorMessage = string.Empty;
        SaveSetting(s => s.FallbackImagePath = path);
        FallbackImagePath = path;
        StatusMessage = "تصویر جایگزین ذخیره شد.";
    }

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        SecurityWarning = string.Empty;

        try
        {
            var info = await _authService.TestConnectionAsync(ServerUrl);
            ConnectionStatus = "سرور در دسترس است";
            ServerDetails = $"{info.ServerName} — نسخه {info.Version}";

            if (info.IsNonLocalPlainHttp)
            {
                SecurityWarning = "هشدار امنیتی: اتصال HTTP غیررمزگذاری‌شده خارج از شبکهٔ محلی ناامن است؛ استفاده از HTTPS توصیه می‌شود.";
            }
        }
        catch (JellyfinApiException ex)
        {
            ConnectionStatus = "ارتباط برقرار نشد";
            ServerDetails = string.Empty;
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.Error("خطای غیرمنتظره هنگام تست اتصال.", ex);
            ErrorMessage = "خطای غیرمنتظره هنگام تست اتصال رخ داد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LogoutAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            _slideshowService.Stop();
            await _authService.LogoutAsync();
            Libraries.Clear();
            RefreshSessionPresentation();
            ItemsSummary = "برای دریافت عنوان‌ها ابتدا وارد حساب شوید.";
            StatusMessage = "خروج از حساب با موفقیت انجام شد.";
            StateSynchronized?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام خروج از حساب در پنجرهٔ تنظیمات.", ex);
            ErrorMessage = "خروج با خطا مواجه شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadLibrariesAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            await LoadLibrariesInternalAsync();
            StatusMessage = $"{Libraries.Count} کتابخانه دریافت شد.";
        }
        catch (JellyfinApiException ex)
        {
            HandleApiError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyMediaSelectionAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            await ApplyMediaSelectionInternalAsync();
            StateSynchronized?.Invoke(this, EventArgs.Empty);
        }
        catch (JellyfinApiException ex)
        {
            HandleApiError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadLibrariesInternalAsync()
    {
        var savedSelection = _settingsService.Current.SelectedLibraryIds;
        var libraries = await _libraryService.GetAccessibleLibrariesAsync();

        Libraries.Clear();
        foreach (var lib in libraries)
        {
            Libraries.Add(new LibrarySelectionItem
            {
                Id = lib.Id,
                Name = lib.Name,
                CollectionType = lib.CollectionType,
                IsSelected = savedSelection.Count == 0 || savedSelection.Contains(lib.Id, StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    private async Task ApplyMediaSelectionInternalAsync()
    {
        var selectedTypes = new List<string>();
        if (IncludeMovies)
        {
            selectedTypes.Add("Movie");
        }

        if (IncludeSeries)
        {
            selectedTypes.Add("Series");
        }

        if (selectedTypes.Count == 0)
        {
            ErrorMessage = "حداقل یکی از گزینه‌های فیلم یا سریال را انتخاب کنید.";
            return;
        }

        var selectedLibraryIds = Libraries.Where(l => l.IsSelected).Select(l => l.Id).ToList();
        SaveSetting(s =>
        {
            s.SelectedLibraryIds = selectedLibraryIds;
            s.IncludedItemTypes = selectedTypes;
        });

        var items = await _libraryService.GetMediaItemsAsync(selectedLibraryIds, selectedTypes);
        _slideshowService.SetItems(items);

        var withBackdrop = items.Count(i => i.HasBackdrop);
        ItemsSummary = items.Count == 0
            ? "هیچ عنوانی در کتابخانه‌های انتخاب‌شده یافت نشد."
            : $"تعداد کل عنوان‌ها: {items.Count} (دارای Backdrop: {withBackdrop})";
        StatusMessage = "فهرست رسانه‌های اسلایدشو به‌روزرسانی شد.";
    }

    private Task ClearFallbackImageAsync()
    {
        SaveSetting(s => s.FallbackImagePath = null);
        FallbackImagePath = string.Empty;
        StatusMessage = "تصویر جایگزین حذف شد.";
        return Task.CompletedTask;
    }

    private Task ClearCacheAsync()
    {
        _cacheService.ClearCache();
        RefreshCacheSummary();
        StatusMessage = "تمام فایل‌های کش محلی پاک‌سازی شدند.";
        return Task.CompletedTask;
    }

    private Task PruneCacheAsync()
    {
        _cacheService.PruneCache(MaxCacheSizeMb, MaxCacheAgeDays);
        RefreshCacheSummary();
        StatusMessage = "پاک‌سازی هوشمند کش بر اساس محدودیت حجم و عمر انجام شد.";
        return Task.CompletedTask;
    }

    private async Task RestoreWallpaperOnFeatureDisabledAsync()
    {
        try
        {
            var result = await _wallpaperService.RestoreOriginalWallpaperAsync();
            if (result.Succeeded)
            {
                ErrorMessage = string.Empty;
                StatusMessage = result.Message;
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام بازگردانی والپیپر پس از غیرفعال‌شدن قابلیت.", ex);
            ErrorMessage = "بازگردانی والپیپر قبلی با خطا مواجه شد.";
        }
    }

    private async Task ApplyCurrentSlideOnFeatureEnabledAsync(string imagePath)
    {
        try
        {
            var result = await _wallpaperService.ApplyWallpaperAsync(imagePath);
            if (result.Succeeded)
            {
                ErrorMessage = string.Empty;
                StatusMessage = result.Message;
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام اعمال والپیپر پس از فعال‌شدن قابلیت.", ex);
            ErrorMessage = "اعمال والپیپر روی دسکتاپ با خطا مواجه شد.";
        }
    }

    private async Task ReapplyCurrentWallpaperSafeAsync()
    {
        try
        {
            var result = await _wallpaperService.ReapplyCurrentWallpaperAsync();
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام اعمال مجدد حالت چینش والپیپر.", ex);
        }
    }

    private void ValidateAndSaveCustomUrlTemplate()
    {
        if (string.IsNullOrWhiteSpace(CustomUrlTemplate))
        {
            CustomUrlValidationMessage = IsCustomUrlTargetSelected
                ? "الگوی URL را وارد کنید (مثال: {baseUrl}/web/index.html#!/details?id={itemId})."
                : string.Empty;
            SaveSetting(s => s.PlaybackTarget.CustomUrlTemplate = null);
            return;
        }

        if (_itemLauncher.TryValidateCustomUrlTemplate(CustomUrlTemplate, out var error))
        {
            CustomUrlValidationMessage = string.Empty;
            SaveSetting(s => s.PlaybackTarget.CustomUrlTemplate = CustomUrlTemplate.Trim());
        }
        else
        {
            CustomUrlValidationMessage = error ?? "الگوی URL نامعتبر است.";
        }
    }

    private void RefreshSessionPresentation()
    {
        var session = _authService.CurrentSession;
        IsAuthenticated = session.IsAuthenticated;
        ConnectionStatus = session.IsAuthenticated ? $"متصل ({session.Username})" : "متصل نیست";
        ServerDetails = string.IsNullOrWhiteSpace(session.ServerName)
            ? string.Empty
            : $"{session.ServerName} — نسخه {session.ServerVersion}";
        SecurityWarning = session.IsNonLocalPlainHttp
            ? "هشدار امنیتی: اتصال HTTP غیررمزگذاری‌شده خارج از شبکهٔ محلی ناامن است؛ استفاده از HTTPS توصیه می‌شود."
            : string.Empty;
    }

    private void RefreshCacheSummary()
    {
        var stats = _cacheService.GetStats();
        var sizeMb = stats.TotalBytes / (1024.0 * 1024.0);
        CacheSummary = $"فضای اشغال‌شدهٔ کش: {stats.FileCount} تصویر ({sizeMb:F2} مگابایت)";
    }

    private void SaveSetting(Action<AppSettings> mutate)
    {
        var settings = _settingsService.Current;
        mutate(settings);
        _settingsService.Save(settings);
    }

    private void HandleApiError(JellyfinApiException ex)
    {
        ErrorMessage = ex.Message;
        if (ex.ErrorKind == JellyfinErrorKind.Unauthorized)
        {
            IsAuthenticated = false;
            ConnectionStatus = "نشست منقضی شده";
        }
    }

    private void RefreshCommands()
    {
        TestConnectionCommand.RaiseCanExecuteChanged();
        LogoutCommand.RaiseCanExecuteChanged();
        LoadLibrariesCommand.RaiseCanExecuteChanged();
        ApplyMediaSelectionCommand.RaiseCanExecuteChanged();
        ClearFallbackImageCommand.RaiseCanExecuteChanged();
        ClearCacheCommand.RaiseCanExecuteChanged();
        PruneCacheCommand.RaiseCanExecuteChanged();
    }
}
