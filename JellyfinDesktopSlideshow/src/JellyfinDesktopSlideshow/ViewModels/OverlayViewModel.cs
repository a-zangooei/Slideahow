using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;

namespace JellyfinDesktopSlideshow.ViewModels;

public sealed class OverlayViewModel : ViewModelBase
{
    private readonly ISlideshowService _slideshowService;
    private readonly IItemLauncher _itemLauncher;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly IDesktopShortcutService? _desktopShortcutService;
    private readonly ICurrentItemLaunchCoordinator? _launchCoordinator;

    private MediaItem? _currentItem;
    private double _overlaySize = 44;
    private double _overlayOpacity = 0.85;
    private string _toolTipText = "Jellyfin Desktop Slideshow — عنوانی در حال نمایش نیست";

    public AsyncRelayCommand OpenCurrentItemCommand { get; }
    public AsyncRelayCommand CreateOrUpdateDesktopShortcutCommand { get; }
    public AsyncRelayCommand HideOverlayCommand { get; }

    public event EventHandler<ItemLaunchResult>? LaunchCompleted;
    public event EventHandler<DesktopShortcutUpdateResult>? DesktopShortcutCompleted;
    public event EventHandler? HideOverlayRequested;

    public OverlayViewModel(
        ISlideshowService slideshowService,
        IItemLauncher itemLauncher,
        ISettingsService settingsService,
        ILoggingService logger,
        IDesktopShortcutService? desktopShortcutService = null,
        ICurrentItemLaunchCoordinator? launchCoordinator = null)
    {
        _slideshowService = slideshowService;
        _itemLauncher = itemLauncher;
        _settingsService = settingsService;
        _logger = logger;
        _desktopShortcutService = desktopShortcutService;
        _launchCoordinator = launchCoordinator;

        ApplyAppearanceFromSettings();
        OpenCurrentItemCommand = new AsyncRelayCommand(OpenCurrentItemAsync, () => ResolveEffectiveItem() is not null);
        CreateOrUpdateDesktopShortcutCommand = new AsyncRelayCommand(
            CreateOrUpdateDesktopShortcutAsync,
            () => _desktopShortcutService is not null);
        HideOverlayCommand = new AsyncRelayCommand(() =>
        {
            HideOverlayRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });

        _slideshowService.StateChanged += OnSlideshowStateChanged;

        var initialItem = _launchCoordinator?.ResolveCurrentItem(_slideshowService.CurrentState.CurrentItem);
        if (initialItem is not null)
        {
            _currentItem = initialItem;
            UpdateToolTip(initialItem);
            OpenCurrentItemCommand.RaiseCanExecuteChanged();
        }
    }

    public MediaItem? CurrentItem
    {
        get => _currentItem;
        private set
        {
            if (SetProperty(ref _currentItem, value))
            {
                OpenCurrentItemCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasCurrentItem));
            }
        }
    }

    public bool HasCurrentItem => ResolveEffectiveItem() is not null;

    public double OverlaySize
    {
        get => _overlaySize;
        private set => SetProperty(ref _overlaySize, value);
    }

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        private set => SetProperty(ref _overlayOpacity, value);
    }

    public string ToolTipText
    {
        get => _toolTipText;
        private set => SetProperty(ref _toolTipText, value);
    }

    public void ApplyAppearanceFromSettings()
    {
        var settings = _settingsService.Current;
        OverlaySize = Math.Clamp(settings.OverlaySize, 24, 96);
        OverlayOpacity = Math.Clamp(settings.OverlayOpacity, 0.2, 1.0);
    }

    private MediaItem? ResolveEffectiveItem()
        => _currentItem ?? _launchCoordinator?.ResolveCurrentItem();

    private void OnSlideshowStateChanged(object? sender, SlideshowState state)
    {
        var resolved = state.CurrentItem ?? _launchCoordinator?.ResolveCurrentItem();
        CurrentItem = resolved;
        UpdateToolTip(resolved);
    }

    private void UpdateToolTip(MediaItem? item)
    {
        if (item is null)
        {
            ToolTipText = "Jellyfin Desktop Slideshow — عنوانی در حال نمایش نیست";
            return;
        }

        var yearSuffix = item.ProductionYear.HasValue
            ? $" ({item.ProductionYear})"
            : string.Empty;
        ToolTipText = $"باز کردن: {item.Name}{yearSuffix}";
    }

    private async Task OpenCurrentItemAsync()
    {
        var item = ResolveEffectiveItem();
        if (item is null)
        {
            return;
        }

        try
        {
            var result = _launchCoordinator is not null
                ? await _launchCoordinator.LaunchCurrentItemAsync(item)
                : await _itemLauncher.OpenItemAsync(item);
            LaunchCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _logger.Error($"خطا در باز کردن عنوان {item.Name} از آیکون شناور.", ex);
            LaunchCompleted?.Invoke(this, new ItemLaunchResult(false, "خطای غیرمنتظره هنگام باز کردن عنوان رخ داد."));
        }
    }

    private Task CreateOrUpdateDesktopShortcutAsync()
    {
        if (_desktopShortcutService is null)
        {
            return Task.CompletedTask;
        }

        var result = _desktopShortcutService.CreateOrUpdateShortcut();
        if (result.Succeeded)
        {
            var settings = _settingsService.Current;
            settings.ShowDesktopShortcutIcon = true;
            _settingsService.Save(settings);
        }

        DesktopShortcutCompleted?.Invoke(this, result);
        return Task.CompletedTask;
    }
}
