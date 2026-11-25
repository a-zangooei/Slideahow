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

    private MediaItem? _currentItem;
    private double _overlaySize = 44;
    private double _overlayOpacity = 0.85;
    private string _toolTipText = "Jellyfin Desktop Slideshow — عنوانی در حال نمایش نیست";

    public AsyncRelayCommand OpenCurrentItemCommand { get; }

    public event EventHandler<ItemLaunchResult>? LaunchCompleted;

    public OverlayViewModel(
        ISlideshowService slideshowService,
        IItemLauncher itemLauncher,
        ISettingsService settingsService,
        ILoggingService logger)
    {
        _slideshowService = slideshowService;
        _itemLauncher = itemLauncher;
        _settingsService = settingsService;
        _logger = logger;

        ApplyAppearanceFromSettings();
        OpenCurrentItemCommand = new AsyncRelayCommand(OpenCurrentItemAsync, () => CurrentItem is not null);
        _slideshowService.StateChanged += OnSlideshowStateChanged;
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

    public bool HasCurrentItem => CurrentItem is not null;

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

    private void OnSlideshowStateChanged(object? sender, SlideshowState state)
    {
        CurrentItem = state.CurrentItem;
        if (state.CurrentItem is null)
        {
            ToolTipText = "Jellyfin Desktop Slideshow — عنوانی در حال نمایش نیست";
            return;
        }

        var yearSuffix = state.CurrentItem.ProductionYear.HasValue
            ? $" ({state.CurrentItem.ProductionYear})"
            : string.Empty;
        ToolTipText = $"باز کردن: {state.CurrentItem.Name}{yearSuffix}";
    }

    private async Task OpenCurrentItemAsync()
    {
        var item = CurrentItem;
        if (item is null)
        {
            return;
        }

        try
        {
            var result = await _itemLauncher.OpenItemAsync(item);
            LaunchCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _logger.Error($"خطا در باز کردن عنوان {item.Name} از آیکون شناور.", ex);
            LaunchCompleted?.Invoke(this, new ItemLaunchResult(false, "خطای غیرمنتظره هنگام باز کردن عنوان رخ داد."));
        }
    }
}
