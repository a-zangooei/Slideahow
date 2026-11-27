using System.Collections.ObjectModel;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;

namespace JellyfinDesktopSlideshow.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILibraryService _libraryService;
    private readonly IImageService _imageService;
    private readonly ICacheService _cacheService;
    private readonly ISlideshowService _slideshowService;
    private readonly IWallpaperService _wallpaperService;
    private readonly IItemLauncher _itemLauncher;
    private readonly IOverlayService _overlayService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly IDesktopShortcutService? _desktopShortcutService;
    private readonly ICurrentItemLaunchCoordinator? _launchCoordinator;
    private readonly ILocalizationService _localization;

    private CancellationTokenSource? _previewCts;
    private bool _initialized;
    private string _serverUrl = string.Empty;
    private string _username = string.Empty;
    private JellyfinAuthMethod _selectedAuthMethod = JellyfinAuthMethod.ApiKey;
    private string _selectedLanguageCode;
    private string _connectionStatus = string.Empty;
    private string _serverDetails = string.Empty;
    private string _securityWarning = string.Empty;
    private string _errorMessage = string.Empty;
    private string _itemsSummary = string.Empty;
    private string _previewTitle = string.Empty;
    private string _previewStatus = string.Empty;
    private string _currentSlideItemId = "-";
    private string? _previewImagePath;
    private string? _lastAppliedSlidePath;
    private string _cacheSummary = string.Empty;
    private string _wallpaperStatus = string.Empty;
    private string _monitorsSummary = string.Empty;
    private bool _isSlideshowRunning;
    private bool _isSlideshowPaused;
    private bool _canGoPrevious;
    private bool _isOverlayVisible = true;
    private string _launchStatus = string.Empty;
    private string? _pendingFallbackBrowserUrl;
    private MediaItem? _selectedMediaItem;
    private bool _isBusy;
    private bool _isAuthenticated;
    private ServerPublicInfo? _lastTestedServerInfo;
    private bool _hasAttemptedMediaLoad;

    public ObservableCollection<MediaItem> MediaItems { get; } = [];

    public IReadOnlyList<LanguageDefinition> AvailableLanguages => _localization.AvailableLanguages;

    public IReadOnlyList<KeyValuePair<JellyfinAuthMethod, string>> AuthMethodOptions => BuildAuthMethodOptions();

    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand RefreshMediaCommand { get; }
    public AsyncRelayCommand StartSlideshowCommand { get; }
    public AsyncRelayCommand TogglePauseCommand { get; }
    public AsyncRelayCommand NextSlideCommand { get; }
    public AsyncRelayCommand PreviousSlideCommand { get; }
    public AsyncRelayCommand ApplyCurrentWallpaperCommand { get; }
    public AsyncRelayCommand OpenCurrentItemCommand { get; }
    public AsyncRelayCommand CreateOrUpdateDesktopShortcutCommand { get; }
    public AsyncRelayCommand OpenFallbackBrowserCommand { get; }
    public AsyncRelayCommand ToggleOverlayCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public AsyncRelayCommand MinimizeToTrayCommand { get; }
    public AsyncRelayCommand ExitApplicationCommand { get; }

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? MinimizeToTrayRequested;
    public event EventHandler? ExitApplicationRequested;
    public event EventHandler? DesktopShortcutSynchronized;
    public event EventHandler? LanguageSynchronized;

    public MainViewModel(
        IAuthenticationService authService,
        ILibraryService libraryService,
        IImageService imageService,
        ICacheService cacheService,
        ISlideshowService slideshowService,
        IWallpaperService wallpaperService,
        IItemLauncher itemLauncher,
        IOverlayService overlayService,
        ISettingsService settingsService,
        ILoggingService logger,
        IDesktopShortcutService? desktopShortcutService = null,
        ICurrentItemLaunchCoordinator? launchCoordinator = null,
        ILocalizationService? localization = null)
    {
        _authService = authService;
        _libraryService = libraryService;
        _imageService = imageService;
        _cacheService = cacheService;
        _slideshowService = slideshowService;
        _wallpaperService = wallpaperService;
        _itemLauncher = itemLauncher;
        _overlayService = overlayService;
        _settingsService = settingsService;
        _logger = logger;
        _desktopShortcutService = desktopShortcutService;
        _launchCoordinator = launchCoordinator;
        _localization = localization ?? AppLocalization.Current;

        var settings = _settingsService.Load();
        _serverUrl = settings.ServerUrl;
        _username = settings.Username ?? string.Empty;
        _selectedAuthMethod = string.Equals(settings.AuthMethod, AppSettings.PasswordAuthMethod, StringComparison.OrdinalIgnoreCase)
            ? JellyfinAuthMethod.UsernamePassword
            : JellyfinAuthMethod.ApiKey;
        _selectedLanguageCode = _localization.NormalizeLanguageCode(settings.Language);
        if (!string.Equals(_localization.CurrentLanguage.Code, _selectedLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            _localization.SetLanguage(_selectedLanguageCode);
        }

        _connectionStatus = _localization.GetString("Status.Disconnected");
        _itemsSummary = _localization.GetString("Main.ItemsSummary.NotLoaded");
        _previewTitle = _localization.GetString("Main.Preview.DefaultTitle");
        _previewStatus = _localization.GetString("Main.Preview.DefaultStatus");
        _wallpaperStatus = _localization.GetString("Main.WallpaperStatus.Initial");

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        RefreshMediaCommand = new AsyncRelayCommand(RefreshMediaAsync, () => !IsBusy && IsAuthenticated);
        StartSlideshowCommand = new AsyncRelayCommand(() => _slideshowService.StartAsync(), () => MediaItems.Count > 0 && !IsBusy);
        TogglePauseCommand = new AsyncRelayCommand(TogglePauseAsync, () => IsSlideshowRunning);
        NextSlideCommand = new AsyncRelayCommand(() => _slideshowService.NextAsync(), () => MediaItems.Count > 0 && !IsBusy);
        PreviousSlideCommand = new AsyncRelayCommand(() => _slideshowService.PreviousAsync(), () => CanGoPrevious && !IsBusy);
        ApplyCurrentWallpaperCommand = new AsyncRelayCommand(ApplyCurrentWallpaperAsync, () => !string.IsNullOrWhiteSpace(PreviewImagePath));
        OpenCurrentItemCommand = new AsyncRelayCommand(OpenCurrentItemAsync, () => ResolveCurrentActionItem() is not null && !IsBusy);
        CreateOrUpdateDesktopShortcutCommand = new AsyncRelayCommand(
            CreateOrUpdateDesktopShortcutAsync,
            () => _desktopShortcutService is not null && !IsBusy);
        OpenFallbackBrowserCommand = new AsyncRelayCommand(OpenFallbackBrowserAsync, () => HasPendingFallbackBrowserUrl);
        ToggleOverlayCommand = new AsyncRelayCommand(ToggleOverlayAsync);
        OpenSettingsCommand = new AsyncRelayCommand(() =>
        {
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        MinimizeToTrayCommand = new AsyncRelayCommand(() =>
        {
            MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        ExitApplicationCommand = new AsyncRelayCommand(() =>
        {
            ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });

        _slideshowService.StateChanged += OnSlideshowStateChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshCacheSummary();
        RefreshDisplayConfiguration();
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
                var settings = _settingsService.Current;
                settings.Language = normalized;
                _settingsService.Save(settings);
                _localization.SetLanguage(normalized);
                OnPropertyChanged();
                LanguageSynchronized?.Invoke(this, EventArgs.Empty);
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
                var settings = _settingsService.Current;
                settings.AuthMethod = value == JellyfinAuthMethod.UsernamePassword
                    ? AppSettings.PasswordAuthMethod
                    : AppSettings.ApiKeyAuthMethod;
                _settingsService.Save(settings);
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

    public string ItemsSummary
    {
        get => _itemsSummary;
        private set => SetProperty(ref _itemsSummary, value);
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
                OnPropertyChanged(nameof(NeedsQuickLogin));
                RefreshCommands();
            }
        }
    }

    public bool NeedsQuickLogin => !IsAuthenticated;

    public MediaItem? SelectedMediaItem
    {
        get => _selectedMediaItem;
        set
        {
            if (SetProperty(ref _selectedMediaItem, value))
            {
                OpenCurrentItemCommand.RaiseCanExecuteChanged();
                _ = LoadSelectedBackdropAsync();
            }
        }
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    public string PreviewStatus
    {
        get => _previewStatus;
        private set => SetProperty(ref _previewStatus, value);
    }

    public string? PreviewImagePath
    {
        get => _previewImagePath;
        private set
        {
            if (SetProperty(ref _previewImagePath, value))
            {
                ApplyCurrentWallpaperCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WallpaperStatus
    {
        get => _wallpaperStatus;
        private set => SetProperty(ref _wallpaperStatus, value);
    }

    public string MonitorsSummary
    {
        get => _monitorsSummary;
        private set => SetProperty(ref _monitorsSummary, value);
    }

    public string CacheSummary
    {
        get => _cacheSummary;
        private set => SetProperty(ref _cacheSummary, value);
    }

    public string CurrentSlideItemId
    {
        get => _currentSlideItemId;
        private set => SetProperty(ref _currentSlideItemId, value);
    }

    public bool IsSlideshowRunning
    {
        get => _isSlideshowRunning;
        private set
        {
            if (SetProperty(ref _isSlideshowRunning, value))
            {
                TogglePauseCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(PauseResumeButtonText));
            }
        }
    }

    public bool IsSlideshowPaused
    {
        get => _isSlideshowPaused;
        private set
        {
            if (SetProperty(ref _isSlideshowPaused, value))
            {
                OnPropertyChanged(nameof(PauseResumeButtonText));
            }
        }
    }

    public bool CanGoPrevious
    {
        get => _canGoPrevious;
        private set
        {
            if (SetProperty(ref _canGoPrevious, value))
            {
                PreviousSlideCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PauseResumeButtonText => IsSlideshowPaused
        ? _localization.GetString("Main.Controls.Resume")
        : _localization.GetString("Main.Controls.Pause");

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        private set
        {
            if (SetProperty(ref _isOverlayVisible, value))
            {
                OnPropertyChanged(nameof(OverlayToggleButtonText));
            }
        }
    }

    public string OverlayToggleButtonText => IsOverlayVisible
        ? _localization.GetString("Main.Controls.HideOverlay")
        : _localization.GetString("Main.Controls.ShowOverlay");

    public string LaunchStatus
    {
        get => _launchStatus;
        private set => SetProperty(ref _launchStatus, value);
    }

    public string? PendingFallbackBrowserUrl
    {
        get => _pendingFallbackBrowserUrl;
        private set
        {
            if (SetProperty(ref _pendingFallbackBrowserUrl, value))
            {
                OnPropertyChanged(nameof(HasPendingFallbackBrowserUrl));
                OpenFallbackBrowserCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasPendingFallbackBrowserUrl => !string.IsNullOrWhiteSpace(PendingFallbackBrowserUrl);

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        IsBusy = true;
        ErrorMessage = string.Empty;
        IsOverlayVisible = _overlayService.IsVisible;

        try
        {
            var session = await _authService.TryRestoreSessionAsync();
            ApplySessionState(session);

            if (session.IsAuthenticated)
            {
                await LoadMediaItemsInternalAsync();
                if (MediaItems.Count > 0 && !IsSlideshowRunning)
                {
                    await _slideshowService.StartAsync();
                }
            }
            else if (!string.IsNullOrWhiteSpace(session.WarningOrError))
            {
                ErrorMessage = session.WarningOrError;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("خطا هنگام راه‌اندازی اولیه.", ex);
            ErrorMessage = _localization.GetString("Auth.Error.UnexpectedRestore");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> LoginWithApiKeyAsync(string apiKey)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var session = await _authService.LoginWithApiKeyAsync(ServerUrl, apiKey);
            ApplySessionState(session);
            await LoadMediaItemsInternalAsync();
            if (MediaItems.Count > 0 && !IsSlideshowRunning)
            {
                await _slideshowService.StartAsync();
            }

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
            _logger.Error("خطای غیرمنتظره هنگام اتصال با کلید API.", ex);
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

        try
        {
            var session = await _authService.LoginAsync(ServerUrl, Username, password);
            ApplySessionState(session);
            await LoadMediaItemsInternalAsync();
            if (MediaItems.Count > 0 && !IsSlideshowRunning)
            {
                await _slideshowService.StartAsync();
            }

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
            _logger.Error("خطای غیرمنتظره هنگام ورود.", ex);
            ErrorMessage = _localization.GetString("Auth.Error.UnexpectedLogin");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SyncFromSettingsWindowAsync()
    {
        var settings = _settingsService.Current;
        ServerUrl = settings.ServerUrl;
        Username = settings.Username ?? string.Empty;
        _selectedAuthMethod = string.Equals(settings.AuthMethod, AppSettings.PasswordAuthMethod, StringComparison.OrdinalIgnoreCase)
            ? JellyfinAuthMethod.UsernamePassword
            : JellyfinAuthMethod.ApiKey;
        OnPropertyChanged(nameof(SelectedAuthMethod));
        OnPropertyChanged(nameof(IsApiKeyAuthSelected));
        OnPropertyChanged(nameof(IsPasswordAuthSelected));

        var currentLangCode = _localization.CurrentLanguage.Code;
        if (!string.Equals(_selectedLanguageCode, currentLangCode, StringComparison.OrdinalIgnoreCase))
        {
            _selectedLanguageCode = currentLangCode;
            OnPropertyChanged(nameof(SelectedLanguageCode));
        }

        ApplySessionState(_authService.CurrentSession);
        IsOverlayVisible = _overlayService.IsVisible;
        RefreshCacheSummary();
        MonitorsSummary = _wallpaperService.GetMonitorsSummary();
        if (!settings.ApplyToDesktopWallpaper)
        {
            _lastAppliedSlidePath = null;
            WallpaperStatus = _localization.GetString("Main.WallpaperStatus.AutoDisabled");
        }

        if (!IsAuthenticated)
        {
            CancelPendingPreview();
            MediaItems.Clear();
            _hasAttemptedMediaLoad = false;
            SelectedMediaItem = null;
            PreviewImagePath = null;
            _lastAppliedSlidePath = null;
            CurrentSlideItemId = "-";
            PreviewTitle = _localization.GetString("Main.Preview.DefaultTitle");
            PreviewStatus = _localization.GetString("Main.Preview.SignInRequired");
            ItemsSummary = _localization.GetString("Main.ItemsSummary.Disconnected");
            RefreshCommands();
            return;
        }

        try
        {
            await LoadMediaItemsInternalAsync();
        }
        catch (JellyfinApiException ex)
        {
            HandleApiError(ex);
        }
    }

    public void RefreshDisplayConfiguration()
    {
        MonitorsSummary = _wallpaperService.GetMonitorsSummary();
        _overlayService.RefreshFromSettings();
        if (_settingsService.Current.ApplyToDesktopWallpaper &&
            !string.IsNullOrWhiteSpace(_wallpaperService.LastAppliedWallpaperPath))
        {
            _ = ReapplyWallpaperSafeAsync();
        }
    }

    public Task TogglePauseAsync()
    {
        _slideshowService.TogglePause();
        return Task.CompletedTask;
    }

    public Task NextSlideAsync() => _slideshowService.NextAsync();

    public Task OpenCurrentItemFromExternalAsync() => OpenCurrentItemAsync();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var currentLangCode = _localization.CurrentLanguage.Code;
        if (!string.Equals(_selectedLanguageCode, currentLangCode, StringComparison.OrdinalIgnoreCase))
        {
            _selectedLanguageCode = currentLangCode;
            OnPropertyChanged(nameof(SelectedLanguageCode));
        }

        OnPropertyChanged(nameof(AvailableLanguages));
        OnPropertyChanged(nameof(AuthMethodOptions));
        OnPropertyChanged(nameof(PauseResumeButtonText));
        OnPropertyChanged(nameof(OverlayToggleButtonText));

        if (_authService.CurrentSession.IsAuthenticated)
        {
            ApplySessionState(_authService.CurrentSession);
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
        RefreshItemsSummaryText();

        if (SelectedMediaItem is null && _slideshowService.CurrentState.CurrentItem is null)
        {
            PreviewTitle = _localization.GetString("Main.Preview.DefaultTitle");
            PreviewStatus = IsAuthenticated
                ? _localization.GetString("Main.Preview.DefaultStatus")
                : _localization.GetString("Main.Preview.SignInRequired");
        }

        if (!_settingsService.Current.ApplyToDesktopWallpaper)
        {
            WallpaperStatus = _localization.GetString("Main.WallpaperStatus.AutoDisabled");
        }
        else if (string.IsNullOrWhiteSpace(_wallpaperService.LastAppliedWallpaperPath))
        {
            WallpaperStatus = _localization.GetString("Main.WallpaperStatus.Initial");
        }
    }

    private IReadOnlyList<KeyValuePair<JellyfinAuthMethod, string>> BuildAuthMethodOptions()
        =>
        [
            new(JellyfinAuthMethod.ApiKey, _localization.GetString("AuthMethod.ApiKey")),
            new(JellyfinAuthMethod.UsernamePassword, _localization.GetString("AuthMethod.UsernamePassword"))
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

    private async Task RefreshMediaAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            await LoadMediaItemsInternalAsync();
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

    private async Task LoadMediaItemsInternalAsync()
    {
        var settings = _settingsService.Current;
        var selectedLibraryIds = settings.SelectedLibraryIds;
        var selectedTypes = settings.IncludedItemTypes.Count > 0
            ? settings.IncludedItemTypes
            : ["Movie", "Series"];

        if (selectedLibraryIds.Count == 0)
        {
            var libraries = await _libraryService.GetAccessibleLibrariesAsync();
            selectedLibraryIds = libraries.Select(l => l.Id).ToList();
        }

        var items = await _libraryService.GetMediaItemsAsync(selectedLibraryIds, selectedTypes);
        MediaItems.Clear();
        foreach (var item in items)
        {
            MediaItems.Add(item);
        }

        _hasAttemptedMediaLoad = true;
        _slideshowService.SetItems(items);
        RefreshCommands();
        RefreshItemsSummaryText();
    }

    private void RefreshItemsSummaryText()
    {
        if (!IsAuthenticated && !_hasAttemptedMediaLoad)
        {
            ItemsSummary = _localization.GetString("Main.ItemsSummary.Disconnected");
            return;
        }

        if (!_hasAttemptedMediaLoad)
        {
            ItemsSummary = _localization.GetString("Main.ItemsSummary.NotLoaded");
            return;
        }

        var withBackdrop = MediaItems.Count(i => i.HasBackdrop);
        ItemsSummary = MediaItems.Count == 0
            ? _localization.GetString("Media.NoneFound")
            : _localization.Format("Media.SummaryFormat", MediaItems.Count, withBackdrop);
    }

    private async Task LoadSelectedBackdropAsync()
    {
        var item = SelectedMediaItem;
        if (item is null)
        {
            return;
        }

        CancelPendingPreview();
        var cts = new CancellationTokenSource();
        _previewCts = cts;

        PreviewTitle = BidiTextFormatter.FormatTitle(item.Name, item.ProductionYear);
        PreviewStatus = _localization.GetString("Main.Preview.Loading");

        try
        {
            var result = await _imageService.ResolveBackdropAsync(item, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            PreviewImagePath = result.HasImage ? result.LocalFilePath : null;
            CurrentSlideItemId = item.Id;
            PreviewStatus = result.Message;
            RefreshCacheSummary();
        }
        catch (OperationCanceledException)
        {
            // Newer item selected.
        }
        catch (Exception ex)
        {
            _logger.Error($"خطا در بارگذاری پیش‌نمایش تصویر {item.Name}.", ex);
            PreviewImagePath = null;
            PreviewStatus = _localization.GetString("Main.Preview.LoadError");
        }
    }

    private async Task OpenCurrentItemAsync()
    {
        var item = ResolveCurrentActionItem();
        if (item is null)
        {
            LaunchStatus = _localization.GetString("Launch.Error.MissingItem");
            return;
        }

        PendingFallbackBrowserUrl = null;
        LaunchStatus = _localization.Format("Launch.Status.OpeningFormat", BidiTextFormatter.Isolate(item.Name));

        var result = _launchCoordinator is not null
            ? await _launchCoordinator.LaunchCurrentItemAsync(item)
            : await _itemLauncher.OpenItemAsync(item);
        LaunchStatus = result.Message;

        if (!result.Succeeded && result.CanFallbackToBrowser && !string.IsNullOrWhiteSpace(result.FallbackBrowserUrl))
        {
            PendingFallbackBrowserUrl = result.FallbackBrowserUrl;
        }
    }

    private Task CreateOrUpdateDesktopShortcutAsync()
    {
        if (_desktopShortcutService is null)
        {
            return Task.CompletedTask;
        }

        ErrorMessage = string.Empty;
        var result = _desktopShortcutService.CreateOrUpdateShortcut();
        if (result.Succeeded)
        {
            var settings = _settingsService.Current;
            settings.ShowDesktopShortcutIcon = true;
            _settingsService.Save(settings);
            LaunchStatus = result.Message;
            DesktopShortcutSynchronized?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ErrorMessage = result.Message;
            LaunchStatus = result.Message;
        }

        return Task.CompletedTask;
    }

    private Task OpenFallbackBrowserAsync()
    {
        if (string.IsNullOrWhiteSpace(PendingFallbackBrowserUrl))
        {
            return Task.CompletedTask;
        }

        var opened = _itemLauncher.OpenValidatedBrowserUrl(PendingFallbackBrowserUrl);
        LaunchStatus = opened
            ? _localization.GetString("Launch.Status.FallbackBrowserOpened")
            : _localization.GetString("Launch.Error.BrowserFailed");

        if (opened)
        {
            PendingFallbackBrowserUrl = null;
        }

        return Task.CompletedTask;
    }

    private Task ToggleOverlayAsync()
    {
        var settings = _settingsService.Current;
        if (_overlayService.IsVisible)
        {
            _overlayService.Hide();
            settings.ShowOverlayIcon = false;
        }
        else
        {
            _overlayService.Show();
            settings.ShowOverlayIcon = true;
        }

        _settingsService.Save(settings);
        IsOverlayVisible = _overlayService.IsVisible;
        DesktopShortcutSynchronized?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void OnSlideshowStateChanged(object? sender, SlideshowState state)
    {
        IsSlideshowRunning = state.IsRunning;
        IsSlideshowPaused = state.IsPaused;
        CanGoPrevious = state.CanGoPrevious;
        OpenCurrentItemCommand.RaiseCanExecuteChanged();

        if (state.CurrentItem is not null)
        {
            PreviewTitle = BidiTextFormatter.FormatTitle(state.CurrentItem.Name, state.CurrentItem.ProductionYear);
            CurrentSlideItemId = state.CurrentItem.Id;
        }
        else if (!state.IsRunning)
        {
            CurrentSlideItemId = "-";
        }

        if (!string.IsNullOrWhiteSpace(state.CurrentImagePath))
        {
            PreviewImagePath = state.CurrentImagePath;
            if (_settingsService.Current.ApplyToDesktopWallpaper &&
                !string.Equals(_lastAppliedSlidePath, state.CurrentImagePath, StringComparison.OrdinalIgnoreCase))
            {
                _lastAppliedSlidePath = state.CurrentImagePath;
                _ = ApplyWallpaperSafeAsync(state.CurrentImagePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(state.StatusMessage))
        {
            PreviewStatus = state.StatusMessage;
        }

        RefreshCacheSummary();
    }

    private async Task ApplyCurrentWallpaperAsync()
    {
        if (string.IsNullOrWhiteSpace(PreviewImagePath))
        {
            return;
        }

        var itemToRecord = SelectedMediaItem ?? _slideshowService.CurrentState.CurrentItem;
        if (itemToRecord is not null)
        {
            _launchCoordinator?.RecordDisplayedItem(itemToRecord);
        }

        await ApplyWallpaperSafeAsync(PreviewImagePath);
        OpenCurrentItemCommand.RaiseCanExecuteChanged();
    }

    private async Task ApplyWallpaperSafeAsync(string path)
    {
        var result = await _wallpaperService.ApplyWallpaperAsync(path);
        WallpaperStatus = result.Message;
    }

    private async Task ReapplyWallpaperSafeAsync()
    {
        var result = await _wallpaperService.ReapplyCurrentWallpaperAsync();
        if (result.Succeeded)
        {
            WallpaperStatus = _localization.Format("Wallpaper.Status.DisplayReappliedFormat", result.Message);
        }
    }

    private MediaItem? ResolveCurrentActionItem()
        => _launchCoordinator?.ResolveCurrentItem(SelectedMediaItem)
           ?? _slideshowService.CurrentState.CurrentItem
           ?? SelectedMediaItem;

    private void CancelPendingPreview()
    {
        var existing = _previewCts;
        _previewCts = null;
        if (existing is not null)
        {
            existing.Cancel();
            existing.Dispose();
        }
    }

    private void RefreshCacheSummary()
    {
        var stats = _cacheService.GetStats();
        var sizeMb = stats.TotalBytes / (1024.0 * 1024.0);
        CacheSummary = _localization.Format("Main.CacheSummaryFormat", stats.FileCount, sizeMb);
    }

    private void ApplySessionState(ConnectionSessionState session)
    {
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
        RefreshMediaCommand.RaiseCanExecuteChanged();
        StartSlideshowCommand.RaiseCanExecuteChanged();
        TogglePauseCommand.RaiseCanExecuteChanged();
        NextSlideCommand.RaiseCanExecuteChanged();
        PreviousSlideCommand.RaiseCanExecuteChanged();
        ApplyCurrentWallpaperCommand.RaiseCanExecuteChanged();
        OpenCurrentItemCommand.RaiseCanExecuteChanged();
        CreateOrUpdateDesktopShortcutCommand.RaiseCanExecuteChanged();
        OpenFallbackBrowserCommand.RaiseCanExecuteChanged();
        ToggleOverlayCommand.RaiseCanExecuteChanged();
    }
}
