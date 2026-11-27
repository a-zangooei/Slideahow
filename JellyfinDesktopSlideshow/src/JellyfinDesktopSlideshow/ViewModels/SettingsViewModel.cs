using System.Collections.ObjectModel;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
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
    private readonly IAppCleanupService? _cleanupService;
    private readonly ILocalizationService _localization;

    private string _serverUrl = string.Empty;
    private string _username = string.Empty;
    private JellyfinAuthMethod _selectedAuthMethod = JellyfinAuthMethod.ApiKey;
    private string _selectedLanguageCode;
    private string _connectionStatus = string.Empty;
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
    private ServerPublicInfo? _lastTestedServerInfo;
    private bool _hasAttemptedMediaLoad;

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
    private bool _filterNonWallpaperAspectRatios = true;
    private BackdropAspectFilterPreset _selectedBackdropAspectFilterPreset = BackdropAspectFilterPreset.Standard;
    private double _minBackdropAspectRatio = 1.25;
    private double _maxBackdropAspectRatio = 2.80;

    private bool _launchOnWindowsStartup;
    private bool _startMinimizedToTray;
    private bool _minimizeToTrayOnClose = true;

    // Tab 8: Cleanup & Footprints
    private bool _cleanBackdropCache = true;
    private bool _cleanWallpaperStagingCache = true;
    private bool _cleanLogsAndHiddenFiles;
    private bool _cleanCredentialsAndSession;
    private bool _cleanOsTraces;
    private bool _restoreOriginalWallpaperOnCleanup = true;
    private bool _cleanResetSettingsFile;
    private AppCleanupFootprint? _lastFootprint;
    private string _cleanupCacheSummaryLine = string.Empty;
    private string _cleanupHiddenSummaryLine = string.Empty;
    private string _cleanupOsSummaryLine = string.Empty;
    private string _cleanupRootSummaryLine = string.Empty;
    private bool _hasAnyCleanupFootprint;

    public ObservableCollection<LibrarySelectionItem> Libraries { get; } = [];

    public IReadOnlyList<LanguageDefinition> AvailableLanguages => _localization.AvailableLanguages;

    public IReadOnlyList<KeyValuePair<WallpaperFitMode, string>> WallpaperFitModes => BuildWallpaperFitModes();
    public IReadOnlyList<KeyValuePair<OfflineBehaviorMode, string>> OfflineBehaviorOptions => BuildOfflineBehaviorOptions();
    public IReadOnlyList<KeyValuePair<PlaybackTargetType, string>> PlaybackTargetOptions => BuildPlaybackTargetOptions();
    public IReadOnlyList<KeyValuePair<DesktopIconMode, string>> DesktopIconModes => BuildDesktopIconModes();
    public IReadOnlyList<KeyValuePair<BackdropAspectFilterPreset, string>> BackdropAspectFilterPresets => BuildBackdropAspectFilterPresets();
    public IReadOnlyList<KeyValuePair<JellyfinAuthMethod, string>> AuthMethodOptions => BuildAuthMethodOptions();

    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public AsyncRelayCommand LoadLibrariesCommand { get; }
    public AsyncRelayCommand ApplyMediaSelectionCommand { get; }
    public AsyncRelayCommand ClearFallbackImageCommand { get; }
    public AsyncRelayCommand ClearCacheCommand { get; }
    public AsyncRelayCommand PruneCacheCommand { get; }
    public AsyncRelayCommand RefreshFootprintCommand { get; }
    public AsyncRelayCommand QuickClearImageCacheCommand { get; }
    public AsyncRelayCommand QuickClearLogsAndHiddenCommand { get; }
    public AsyncRelayCommand CleanSelectedTracesCommand { get; }
    public AsyncRelayCommand FullWipeAllTracesCommand { get; }

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
        ILoggingService logger,
        IAppCleanupService? cleanupService = null,
        ILocalizationService? localization = null)
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
        _cleanupService = cleanupService;
        _localization = localization ?? AppLocalization.Current;

        _selectedLanguageCode = _localization.NormalizeLanguageCode(_settingsService.Current.Language);
        _connectionStatus = _localization.GetString("Status.Disconnected");

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => !IsBusy && IsAuthenticated);
        LoadLibrariesCommand = new AsyncRelayCommand(LoadLibrariesAsync, () => !IsBusy && IsAuthenticated);
        ApplyMediaSelectionCommand = new AsyncRelayCommand(ApplyMediaSelectionAsync, () => !IsBusy && IsAuthenticated);
        ClearFallbackImageCommand = new AsyncRelayCommand(ClearFallbackImageAsync, () => !string.IsNullOrWhiteSpace(FallbackImagePath));
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !IsBusy);
        PruneCacheCommand = new AsyncRelayCommand(PruneCacheAsync, () => !IsBusy);
        RefreshFootprintCommand = new AsyncRelayCommand(() =>
        {
            RefreshFootprint();
            return Task.CompletedTask;
        }, () => !IsBusy && _cleanupService is not null);
        QuickClearImageCacheCommand = new AsyncRelayCommand(QuickClearImageCacheAsync, () => !IsBusy && _cleanupService is not null);
        QuickClearLogsAndHiddenCommand = new AsyncRelayCommand(QuickClearLogsAndHiddenAsync, () => !IsBusy && _cleanupService is not null);
        CleanSelectedTracesCommand = new AsyncRelayCommand(ExecuteSelectedCleanupAsync, () => !IsBusy && _cleanupService is not null);
        FullWipeAllTracesCommand = new AsyncRelayCommand(ExecuteFullWipeAsync, () => !IsBusy && _cleanupService is not null);

        _localization.LanguageChanged += OnLanguageChanged;
        LoadFromCurrentSettings();
        RefreshFootprint();
    }

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            var normalized = _localization.NormalizeLanguageCode(value);
            if (!string.Equals(_selectedLanguageCode, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _selectedLanguageCode = normalized;
                SaveSetting(s => s.Language = normalized);
                _localization.SetLanguage(normalized);
                OnPropertyChanged();
                StateSynchronized?.Invoke(this, EventArgs.Empty);
            }
        }
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

    public JellyfinAuthMethod SelectedAuthMethod
    {
        get => _selectedAuthMethod;
        set
        {
            if (SetProperty(ref _selectedAuthMethod, value))
            {
                SaveSetting(s => s.AuthMethod = value == JellyfinAuthMethod.UsernamePassword
                    ? AppSettings.PasswordAuthMethod
                    : AppSettings.ApiKeyAuthMethod);
                OnPropertyChanged(nameof(IsApiKeyAuthSelected));
                OnPropertyChanged(nameof(IsPasswordAuthSelected));
            }
        }
    }

    public bool IsApiKeyAuthSelected => SelectedAuthMethod == JellyfinAuthMethod.ApiKey;
    public bool IsPasswordAuthSelected => SelectedAuthMethod == JellyfinAuthMethod.UsernamePassword;

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
                        StatusMessage = _localization.GetString("Settings.Status.WallpaperAutoDisabled");
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
                    ErrorMessage = _localization.GetString("Settings.Status.ShortcutToggleError");
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
                DesktopIconMode.FloatingOverlay => _localization.GetString("Settings.Status.ModeFloating"),
                DesktopIconMode.DesktopShortcut => _localization.GetString("Settings.Status.ModeShortcut"),
                DesktopIconMode.Both => _localization.GetString("Settings.Status.ModeBoth"),
                _ => _localization.GetString("Settings.Status.ModeDisabled")
            };

            OnPropertyChanged(nameof(SelectedDesktopIconMode));
            RefreshFootprint();
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
                ErrorMessage = _localization.GetString("Settings.Status.ShortcutToggleError");
                _showDesktopShortcutIcon = _desktopShortcutService.IsShortcutPresent();
                OnPropertyChanged(nameof(ShowDesktopShortcutIcon));
                OnPropertyChanged(nameof(SelectedDesktopIconMode));
                return;
            }

            _showDesktopShortcutIcon = value;
            SaveSetting(s => s.ShowDesktopShortcutIcon = value);
            ErrorMessage = string.Empty;
            StatusMessage = value
                ? _localization.GetString("Settings.Status.ShortcutCreated")
                : _localization.GetString("Settings.Status.ShortcutDeleted");
            OnPropertyChanged(nameof(ShowDesktopShortcutIcon));
            OnPropertyChanged(nameof(SelectedDesktopIconMode));
            RefreshFootprint();
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

    public bool FilterNonWallpaperAspectRatios
    {
        get => _filterNonWallpaperAspectRatios;
        set
        {
            if (SetProperty(ref _filterNonWallpaperAspectRatios, value))
            {
                SaveSetting(s => s.FilterNonWallpaperAspectRatios = value);
                OnPropertyChanged(nameof(IsCustomAspectRatioEnabled));
            }
        }
    }

    public BackdropAspectFilterPreset SelectedBackdropAspectFilterPreset
    {
        get => _selectedBackdropAspectFilterPreset;
        set
        {
            if (SetProperty(ref _selectedBackdropAspectFilterPreset, value))
            {
                if (value != BackdropAspectFilterPreset.Custom)
                {
                    var (min, max) = ImageAspectFilter.GetBoundsForPreset(value);
                    _minBackdropAspectRatio = min;
                    _maxBackdropAspectRatio = max;
                    OnPropertyChanged(nameof(MinBackdropAspectRatio));
                    OnPropertyChanged(nameof(MaxBackdropAspectRatio));
                }

                SaveSetting(s =>
                {
                    s.BackdropAspectFilterPreset = value;
                    s.MinBackdropAspectRatio = _minBackdropAspectRatio;
                    s.MaxBackdropAspectRatio = _maxBackdropAspectRatio;
                });

                OnPropertyChanged(nameof(IsCustomAspectRatioEnabled));
            }
        }
    }

    public bool IsCustomAspectRatioEnabled =>
        FilterNonWallpaperAspectRatios &&
        SelectedBackdropAspectFilterPreset == BackdropAspectFilterPreset.Custom;

    public double MinBackdropAspectRatio
    {
        get => _minBackdropAspectRatio;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0.2, 3.0), 2);
            if (SetProperty(ref _minBackdropAspectRatio, clamped))
            {
                var minAllowedMax = Math.Round(Math.Max(1.1, clamped + 0.1), 2);
                if (_maxBackdropAspectRatio < minAllowedMax)
                {
                    _maxBackdropAspectRatio = minAllowedMax;
                    OnPropertyChanged(nameof(MaxBackdropAspectRatio));
                }

                SaveSetting(s =>
                {
                    s.MinBackdropAspectRatio = _minBackdropAspectRatio;
                    s.MaxBackdropAspectRatio = _maxBackdropAspectRatio;
                });
            }
        }
    }

    public double MaxBackdropAspectRatio
    {
        get => _maxBackdropAspectRatio;
        set
        {
            var minAllowedMax = Math.Max(1.1, _minBackdropAspectRatio + 0.1);
            var clamped = Math.Round(Math.Clamp(value, minAllowedMax, 10.0), 2);
            if (SetProperty(ref _maxBackdropAspectRatio, clamped))
            {
                SaveSetting(s => s.MaxBackdropAspectRatio = clamped);
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
                    ErrorMessage = _localization.GetString("Settings.Status.StartupToggleError");
                    _launchOnWindowsStartup = _startupService.IsStartupEnabled();
                    OnPropertyChanged(nameof(LaunchOnWindowsStartup));
                    return;
                }

                SaveSetting(s => s.LaunchOnWindowsStartup = value);
                StatusMessage = value
                    ? _localization.GetString("Settings.Status.StartupEnabled")
                    : _localization.GetString("Settings.Status.StartupDisabled");
                RefreshFootprint();
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

    // Tab 8: Cleanup & Footprints
    public bool CleanupAvailable => _cleanupService is not null;

    public bool CleanBackdropCache
    {
        get => _cleanBackdropCache;
        set => SetProperty(ref _cleanBackdropCache, value);
    }

    public bool CleanWallpaperStagingCache
    {
        get => _cleanWallpaperStagingCache;
        set => SetProperty(ref _cleanWallpaperStagingCache, value);
    }

    public bool CleanLogsAndHiddenFiles
    {
        get => _cleanLogsAndHiddenFiles;
        set => SetProperty(ref _cleanLogsAndHiddenFiles, value);
    }

    public bool CleanCredentialsAndSession
    {
        get => _cleanCredentialsAndSession;
        set => SetProperty(ref _cleanCredentialsAndSession, value);
    }

    public bool CleanOsTraces
    {
        get => _cleanOsTraces;
        set => SetProperty(ref _cleanOsTraces, value);
    }

    public bool RestoreOriginalWallpaperOnCleanup
    {
        get => _restoreOriginalWallpaperOnCleanup;
        set => SetProperty(ref _restoreOriginalWallpaperOnCleanup, value);
    }

    public bool CleanResetSettingsFile
    {
        get => _cleanResetSettingsFile;
        set => SetProperty(ref _cleanResetSettingsFile, value);
    }

    public string CleanupCacheSummaryLine
    {
        get => _cleanupCacheSummaryLine;
        private set => SetProperty(ref _cleanupCacheSummaryLine, value);
    }

    public string CleanupHiddenSummaryLine
    {
        get => _cleanupHiddenSummaryLine;
        private set => SetProperty(ref _cleanupHiddenSummaryLine, value);
    }

    public string CleanupOsSummaryLine
    {
        get => _cleanupOsSummaryLine;
        private set => SetProperty(ref _cleanupOsSummaryLine, value);
    }

    public string CleanupRootSummaryLine
    {
        get => _cleanupRootSummaryLine;
        private set => SetProperty(ref _cleanupRootSummaryLine, value);
    }

    public bool HasAnyCleanupFootprint
    {
        get => _hasAnyCleanupFootprint;
        private set => SetProperty(ref _hasAnyCleanupFootprint, value);
    }

    public async Task InitializeAsync()
    {
        LoadFromCurrentSettings();
        RefreshSessionPresentation();
        RefreshCacheSummary();
        MonitorsSummary = _wallpaperService.GetMonitorsSummary();
        RefreshFootprint();

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
        _selectedAuthMethod = string.Equals(s.AuthMethod, AppSettings.PasswordAuthMethod, StringComparison.OrdinalIgnoreCase)
            ? JellyfinAuthMethod.UsernamePassword
            : JellyfinAuthMethod.ApiKey;
        _selectedLanguageCode = _localization.CurrentLanguage.Code;
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
        _filterNonWallpaperAspectRatios = s.FilterNonWallpaperAspectRatios;
        _selectedBackdropAspectFilterPreset = s.BackdropAspectFilterPreset;
        _minBackdropAspectRatio = s.MinBackdropAspectRatio;
        _maxBackdropAspectRatio = s.MaxBackdropAspectRatio;
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
        RefreshFootprint();
    }

    public async Task<bool> LoginWithApiKeyAsync(string apiKey)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        try
        {
            await _authService.LoginWithApiKeyAsync(ServerUrl, apiKey);
            RefreshSessionPresentation();
            await LoadLibrariesInternalAsync();
            await ApplyMediaSelectionInternalAsync();
            StatusMessage = _localization.GetString("Status.ApiKeyConnectedAndLoaded");
            RefreshFootprint();
            StateSynchronized?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (JellyfinApiException ex)
        {
            ErrorMessage = ex.Message;
            ConnectionStatus = _localization.GetString("Status.ConnectionError");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error("خطای غیرمنتظره هنگام اتصال با کلید API از پنجرهٔ تنظیمات.", ex);
            ErrorMessage = _localization.GetString("Auth.Error.UnexpectedApiKeyLogin");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
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
            StatusMessage = _localization.GetString("Status.LoginConnectedAndLoaded");
            RefreshFootprint();
            StateSynchronized?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (JellyfinApiException ex)
        {
            ErrorMessage = ex.Message;
            ConnectionStatus = _localization.GetString("Status.LoginError");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error("خطای غیرمنتظره هنگام ورود از پنجرهٔ تنظیمات.", ex);
            ErrorMessage = _localization.GetString("Auth.Error.UnexpectedLogin");
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
            ErrorMessage = _localization.GetString("Settings.Status.InvalidFallbackImage");
            return;
        }

        ErrorMessage = string.Empty;
        SaveSetting(s => s.FallbackImagePath = path);
        FallbackImagePath = path;
        StatusMessage = _localization.GetString("Settings.Status.FallbackSaved");
        RefreshFootprint();
    }

    public void RefreshFootprint()
    {
        if (_cleanupService is null)
        {
            HasAnyCleanupFootprint = false;
            CleanupCacheSummaryLine = string.Empty;
            CleanupHiddenSummaryLine = string.Empty;
            CleanupOsSummaryLine = string.Empty;
            CleanupRootSummaryLine = string.Empty;
            return;
        }

        _lastFootprint = _cleanupService.GetFootprint();
        ApplyFootprintPresentation(_lastFootprint);
    }

    public Task<AppCleanupResult?> ExecuteSelectedCleanupAsync()
    {
        var options = new AppCleanupOptions
        {
            ClearBackdropCache = CleanBackdropCache,
            ClearWallpaperStagingCache = CleanWallpaperStagingCache,
            ClearLogsAndHiddenFiles = CleanLogsAndHiddenFiles,
            RemoveCredentialsAndSession = CleanCredentialsAndSession,
            RemoveOsTraces = CleanOsTraces,
            RestoreOriginalWallpaper = RestoreOriginalWallpaperOnCleanup,
            DeleteSettingsAndReset = CleanResetSettingsFile
        };

        return RunCleanupAsync(options);
    }

    public Task<AppCleanupResult?> ExecuteFullWipeAsync()
        => RunCleanupAsync(AppCleanupOptions.FullWipe(RestoreOriginalWallpaperOnCleanup));

    private async Task QuickClearImageCacheAsync()
        => await RunCleanupAsync(new AppCleanupOptions
        {
            ClearBackdropCache = true,
            ClearWallpaperStagingCache = true
        });

    private async Task QuickClearLogsAndHiddenAsync()
        => await RunCleanupAsync(new AppCleanupOptions
        {
            ClearLogsAndHiddenFiles = true
        });

    private async Task<AppCleanupResult?> RunCleanupAsync(AppCleanupOptions options)
    {
        if (_cleanupService is null)
        {
            return null;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        try
        {
            var result = await _cleanupService.CleanAsync(options);

            if (options.RemoveCredentialsAndSession || options.DeleteSettingsAndReset)
            {
                Libraries.Clear();
                _hasAttemptedMediaLoad = false;
                ItemsSummary = _localization.GetString("Settings.Tab2.SignInToLoadItems");
            }

            if (options.DeleteSettingsAndReset)
            {
                LoadFromCurrentSettings();
            }
            else if (options.RemoveCredentialsAndSession || options.RemoveOsTraces)
            {
                SyncDesktopShortcutStateFromSettings();
                _launchOnWindowsStartup = _settingsService.Current.LaunchOnWindowsStartup;
                OnPropertyChanged(nameof(LaunchOnWindowsStartup));
            }

            RefreshSessionPresentation();
            RefreshCacheSummary();
            RefreshFootprint();

            if (result.Succeeded)
            {
                ErrorMessage = string.Empty;
                StatusMessage = result.Message;
            }
            else
            {
                ErrorMessage = result.Message;
            }

            StateSynchronized?.Invoke(this, EventArgs.Empty);
            return result;
        }
        catch (Exception ex)
        {
            if (!options.ClearLogsAndHiddenFiles)
            {
                _logger.Error("خطا هنگام پاک‌سازی داده‌های برنامه.", ex);
            }

            ErrorMessage = ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFootprintPresentation(AppCleanupFootprint footprint)
    {
        var yes = _localization.GetString("Settings.Tab8.Yes");
        var no = _localization.GetString("Settings.Tab8.No");

        CleanupCacheSummaryLine = _localization.Format(
            "Settings.Tab8.Footprint.CacheLine",
            footprint.BackdropCacheFiles,
            footprint.BackdropCacheBytes / (1024.0 * 1024.0),
            footprint.WallpaperStagingFiles,
            footprint.WallpaperStagingBytes / (1024.0 * 1024.0));

        CleanupHiddenSummaryLine = _localization.Format(
            "Settings.Tab8.Footprint.HiddenLine",
            footprint.LogFiles,
            footprint.BackupFiles,
            footprint.HasIconFile ? yes : no,
            footprint.HasCurrentItemFile ? yes : no);

        CleanupOsSummaryLine = _localization.Format(
            "Settings.Tab8.Footprint.OsLine",
            footprint.HasSettingsFile ? yes : no,
            footprint.HasWallpaperStateFile ? yes : no,
            footprint.HasSavedCredential ? yes : no,
            footprint.HasStartupRegistryEntry ? yes : no,
            footprint.HasDesktopShortcut ? yes : no);

        CleanupRootSummaryLine = footprint.RootDirectoryExists
            ? _localization.Format(
                "Settings.Tab8.Footprint.RootPresentFormat",
                BidiTextFormatter.IsolateLtr(footprint.RootDirectory))
            : _localization.GetString("Settings.Tab8.Footprint.RootClean");

        HasAnyCleanupFootprint = footprint.HasAnyFootprint;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var currentLangCode = _localization.CurrentLanguage.Code;
        if (!string.Equals(_selectedLanguageCode, currentLangCode, StringComparison.OrdinalIgnoreCase))
        {
            _selectedLanguageCode = currentLangCode;
            OnPropertyChanged(nameof(SelectedLanguageCode));
        }

        OnPropertyChanged(nameof(AvailableLanguages));
        OnPropertyChanged(nameof(WallpaperFitModes));
        OnPropertyChanged(nameof(OfflineBehaviorOptions));
        OnPropertyChanged(nameof(PlaybackTargetOptions));
        OnPropertyChanged(nameof(DesktopIconModes));
        OnPropertyChanged(nameof(BackdropAspectFilterPresets));
        OnPropertyChanged(nameof(AuthMethodOptions));

        if (_authService.CurrentSession.IsAuthenticated)
        {
            RefreshSessionPresentation();
        }
        else if (_lastTestedServerInfo is not null)
        {
            ConnectionStatus = _localization.GetString("Status.ServerReachable");
            ServerDetails = BidiTextFormatter.FormatServerDetails(
                _lastTestedServerInfo.ServerName,
                _lastTestedServerInfo.Version,
                _localization.GetString("Settings.VersionLabel"));
            SecurityWarning = _lastTestedServerInfo.IsNonLocalPlainHttp
                ? _localization.GetString("Status.SecurityNonLocalHttpWarning")
                : string.Empty;
        }
        else
        {
            ConnectionStatus = _localization.GetString("Status.Disconnected");
            ServerDetails = string.Empty;
            SecurityWarning = string.Empty;
        }

        RefreshCacheSummary();
        MonitorsSummary = _wallpaperService.GetMonitorsSummary();
        if (_hasAttemptedMediaLoad)
        {
            RefreshItemsSummaryText();
        }

        if (_lastFootprint is not null)
        {
            ApplyFootprintPresentation(_lastFootprint);
        }

        ValidateCustomUrlMessageWithoutSaving();
    }

    private IReadOnlyList<KeyValuePair<WallpaperFitMode, string>> BuildWallpaperFitModes() =>
    [
        new(WallpaperFitMode.Fill, _localization.GetString("Option.WallpaperFit.Fill")),
        new(WallpaperFitMode.Fit, _localization.GetString("Option.WallpaperFit.Fit")),
        new(WallpaperFitMode.Stretch, _localization.GetString("Option.WallpaperFit.Stretch")),
        new(WallpaperFitMode.Center, _localization.GetString("Option.WallpaperFit.Center")),
        new(WallpaperFitMode.Span, _localization.GetString("Option.WallpaperFit.Span"))
    ];

    private IReadOnlyList<KeyValuePair<OfflineBehaviorMode, string>> BuildOfflineBehaviorOptions() =>
    [
        new(OfflineBehaviorMode.UseCacheAndRetry, _localization.GetString("Option.Offline.UseCacheAndRetry")),
        new(OfflineBehaviorMode.KeepCurrentWallpaper, _localization.GetString("Option.Offline.KeepCurrentWallpaper")),
        new(OfflineBehaviorMode.PauseSlideshow, _localization.GetString("Option.Offline.PauseSlideshow"))
    ];

    private IReadOnlyList<KeyValuePair<PlaybackTargetType, string>> BuildPlaybackTargetOptions() =>
    [
        new(PlaybackTargetType.DefaultBrowser, _localization.GetString("Option.PlaybackTarget.DefaultBrowser")),
        new(PlaybackTargetType.JellyfinDesktopClient, _localization.GetString("Option.PlaybackTarget.DesktopClient")),
        new(PlaybackTargetType.CustomUrlTemplate, _localization.GetString("Option.PlaybackTarget.CustomUrl"))
    ];

    private IReadOnlyList<KeyValuePair<DesktopIconMode, string>> BuildDesktopIconModes() =>
    [
        new(DesktopIconMode.FloatingOverlay, _localization.GetString("Option.DesktopIcon.FloatingOverlay")),
        new(DesktopIconMode.DesktopShortcut, _localization.GetString("Option.DesktopIcon.DesktopShortcut")),
        new(DesktopIconMode.Both, _localization.GetString("Option.DesktopIcon.Both")),
        new(DesktopIconMode.Disabled, _localization.GetString("Option.DesktopIcon.Disabled"))
    ];

    private IReadOnlyList<KeyValuePair<BackdropAspectFilterPreset, string>> BuildBackdropAspectFilterPresets() =>
    [
        new(BackdropAspectFilterPreset.Standard, _localization.GetString("Option.AspectPreset.Standard")),
        new(BackdropAspectFilterPreset.StrictWidescreen, _localization.GetString("Option.AspectPreset.StrictWidescreen")),
        new(BackdropAspectFilterPreset.RelaxedBannerOnly, _localization.GetString("Option.AspectPreset.RelaxedBannerOnly")),
        new(BackdropAspectFilterPreset.Custom, _localization.GetString("Option.AspectPreset.Custom"))
    ];

    private IReadOnlyList<KeyValuePair<JellyfinAuthMethod, string>> BuildAuthMethodOptions() =>
    [
        new(JellyfinAuthMethod.ApiKey, _localization.GetString("AuthMethod.ApiKeySettings")),
        new(JellyfinAuthMethod.UsernamePassword, _localization.GetString("AuthMethod.UsernamePasswordShort"))
    ];

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        SecurityWarning = string.Empty;

        try
        {
            var info = await _authService.TestConnectionAsync(ServerUrl);
            _lastTestedServerInfo = info;
            ConnectionStatus = _localization.GetString("Status.ServerReachable");
            ServerDetails = BidiTextFormatter.FormatServerDetails(
                info.ServerName,
                info.Version,
                _localization.GetString("Settings.VersionLabel"));

            if (info.IsNonLocalPlainHttp)
            {
                SecurityWarning = _localization.GetString("Status.SecurityNonLocalHttpWarning");
            }
        }
        catch (JellyfinApiException ex)
        {
            _lastTestedServerInfo = null;
            ConnectionStatus = _localization.GetString("Status.ServerUnreachable");
            ServerDetails = string.Empty;
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.Error("خطای غیرمنتظره هنگام تست اتصال.", ex);
            ErrorMessage = _localization.GetString("Auth.Error.UnexpectedConnection");
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
            _hasAttemptedMediaLoad = false;
            RefreshSessionPresentation();
            ItemsSummary = _localization.GetString("Settings.Tab2.SignInToLoadItems");
            StatusMessage = _localization.GetString("Status.LogoutSuccess");
            RefreshFootprint();
            StateSynchronized?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام خروج از حساب در پنجرهٔ تنظیمات.", ex);
            ErrorMessage = _localization.GetString("Status.LogoutFailed");
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
            StatusMessage = _localization.Format("Settings.Tab2.LibrariesLoadedFormat", Libraries.Count);
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
            ErrorMessage = _localization.GetString("Settings.Tab2.SelectMovieOrSeries");
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
        _hasAttemptedMediaLoad = true;
        _lastLoadedItemsCount = items.Count;
        _lastLoadedBackdropCount = items.Count(i => i.HasBackdrop);

        RefreshItemsSummaryText();
        StatusMessage = _localization.GetString("Settings.Tab2.MediaUpdated");
    }

    private int _lastLoadedItemsCount;
    private int _lastLoadedBackdropCount;

    private void RefreshItemsSummaryText()
    {
        ItemsSummary = _lastLoadedItemsCount == 0
            ? _localization.GetString("Media.NoneFound")
            : _localization.Format("Media.SettingsSummaryFormat", _lastLoadedItemsCount, _lastLoadedBackdropCount);
    }

    private Task ClearFallbackImageAsync()
    {
        SaveSetting(s => s.FallbackImagePath = null);
        FallbackImagePath = string.Empty;
        StatusMessage = _localization.GetString("Settings.Status.FallbackCleared");
        return Task.CompletedTask;
    }

    private Task ClearCacheAsync()
    {
        _cacheService.ClearCache();
        RefreshCacheSummary();
        RefreshFootprint();
        StatusMessage = _localization.GetString("Settings.Status.CacheCleared");
        return Task.CompletedTask;
    }

    private Task PruneCacheAsync()
    {
        _cacheService.PruneCache(MaxCacheSizeMb, MaxCacheAgeDays);
        RefreshCacheSummary();
        RefreshFootprint();
        StatusMessage = _localization.GetString("Settings.Status.CachePruned");
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

            RefreshFootprint();
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام بازگردانی والپیپر پس از غیرفعال‌شدن قابلیت.", ex);
            ErrorMessage = _localization.GetString("Settings.Status.WallpaperRestoreError");
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

            RefreshFootprint();
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام اعمال والپیپر پس از فعال‌شدن قابلیت.", ex);
            ErrorMessage = _localization.GetString("Settings.Status.WallpaperApplyError");
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
                ? _localization.GetString("Validation.CustomUrl.Prompt")
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
            CustomUrlValidationMessage = error ?? _localization.GetString("Validation.CustomUrl.Invalid");
        }
    }

    private void ValidateCustomUrlMessageWithoutSaving()
    {
        if (string.IsNullOrWhiteSpace(CustomUrlTemplate))
        {
            CustomUrlValidationMessage = IsCustomUrlTargetSelected
                ? _localization.GetString("Validation.CustomUrl.Prompt")
                : string.Empty;
            return;
        }

        if (_itemLauncher.TryValidateCustomUrlTemplate(CustomUrlTemplate, out var error))
        {
            CustomUrlValidationMessage = string.Empty;
        }
        else
        {
            CustomUrlValidationMessage = error ?? _localization.GetString("Validation.CustomUrl.Invalid");
        }
    }

    private void RefreshSessionPresentation()
    {
        var session = _authService.CurrentSession;
        IsAuthenticated = session.IsAuthenticated;
        ConnectionStatus = session.IsAuthenticated
            ? (session.IsApiKeySession
                ? _localization.GetString("Status.ConnectedApiKey")
                : _localization.Format("Status.ConnectedUserFormat", BidiTextFormatter.Isolate(session.Username)))
            : _localization.GetString("Status.Disconnected");
        ServerDetails = BidiTextFormatter.FormatServerDetails(
            session.ServerName,
            session.ServerVersion,
            _localization.GetString("Settings.VersionLabel"));
        SecurityWarning = session.IsNonLocalPlainHttp
            ? _localization.GetString("Status.SecurityNonLocalHttpWarning")
            : string.Empty;
    }

    private void RefreshCacheSummary()
    {
        var stats = _cacheService.GetStats();
        var sizeMb = stats.TotalBytes / (1024.0 * 1024.0);
        CacheSummary = _localization.Format("Settings.Tab6.CacheOccupiedFormat", stats.FileCount, sizeMb);
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
            ConnectionStatus = _localization.GetString("Status.SessionExpired");
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
        RefreshFootprintCommand.RaiseCanExecuteChanged();
        QuickClearImageCacheCommand.RaiseCanExecuteChanged();
        QuickClearLogsAndHiddenCommand.RaiseCanExecuteChanged();
        CleanSelectedTracesCommand.RaiseCanExecuteChanged();
        FullWipeAllTracesCommand.RaiseCanExecuteChanged();
    }
}
