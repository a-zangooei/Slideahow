using System.Windows;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.ViewModels;
using JellyfinDesktopSlideshow.Views;

namespace JellyfinDesktopSlideshow.Services;

public sealed class OverlayService : IOverlayService
{
    private readonly OverlayViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly IItemLauncher _itemLauncher;
    private readonly ILoggingService _logger;
    private OverlayWindow? _window;

    public bool IsVisible => _window is { IsVisible: true };

    public OverlayService(
        OverlayViewModel viewModel,
        ISettingsService settingsService,
        IItemLauncher itemLauncher,
        ILoggingService logger)
    {
        _viewModel = viewModel;
        _settingsService = settingsService;
        _itemLauncher = itemLauncher;
        _logger = logger;

        _viewModel.LaunchCompleted += OnLaunchCompleted;
    }

    public void Show()
    {
        if (_window is null)
        {
            _window = new OverlayWindow(_viewModel);
        }

        RefreshFromSettings();
        if (!_window.IsVisible)
        {
            _window.Show();
        }
    }

    public void Hide()
    {
        _window?.Hide();
    }

    public void RefreshFromSettings()
    {
        _viewModel.ApplyAppearanceFromSettings();
        if (_window is null)
        {
            return;
        }

        var settings = _settingsService.Current;
        var size = Math.Clamp(settings.OverlaySize, 24, 96);

        var minLeft = SystemParameters.VirtualScreenLeft;
        var minTop = SystemParameters.VirtualScreenTop;
        var maxLeft = Math.Max(minLeft, minLeft + SystemParameters.VirtualScreenWidth - size);
        var maxTop = Math.Max(minTop, minTop + SystemParameters.VirtualScreenHeight - size);

        _window.Left = Math.Clamp(settings.OverlayLeft, minLeft, maxLeft);
        _window.Top = Math.Clamp(settings.OverlayTop, minTop, maxTop);
    }

    public void Close()
    {
        _viewModel.LaunchCompleted -= OnLaunchCompleted;
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
    }

    private void OnLaunchCompleted(object? sender, ItemLaunchResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        _logger.Warning($"باز کردن عنوان از آیکون شناور ناموفق بود: {result.Message}");

        if (result.CanFallbackToBrowser && !string.IsNullOrWhiteSpace(result.FallbackBrowserUrl))
        {
            var answer = MessageBox.Show(
                $"{result.Message}\n\nآیا می‌خواهید این عنوان در مرورگر پیش‌فرض باز شود؟",
                "Jellyfin Desktop Slideshow",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                _itemLauncher.OpenValidatedBrowserUrl(result.FallbackBrowserUrl);
            }
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
}
