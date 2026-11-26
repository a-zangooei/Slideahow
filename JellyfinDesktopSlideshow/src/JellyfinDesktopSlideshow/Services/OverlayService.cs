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

    public event EventHandler? StateUpdated;

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
        _viewModel.DesktopShortcutCompleted += OnDesktopShortcutCompleted;
        _viewModel.HideOverlayRequested += OnHideOverlayRequested;
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
        _viewModel.DesktopShortcutCompleted -= OnDesktopShortcutCompleted;
        _viewModel.HideOverlayRequested -= OnHideOverlayRequested;
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
    }

    private void OnLaunchCompleted(object? sender, ItemLaunchResult result)
        => LaunchResultDialogHelper.HandleFailure(result, _itemLauncher, _logger, "آیکون شناور");

    private void OnDesktopShortcutCompleted(object? sender, DesktopShortcutUpdateResult result)
    {
        if (!result.Succeeded)
        {
            MessageBox.Show(
                result.Message,
                "Jellyfin Desktop Slideshow",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        StateUpdated?.Invoke(this, EventArgs.Empty);
        MessageBox.Show(
            result.Message,
            "Jellyfin Desktop Slideshow",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnHideOverlayRequested(object? sender, EventArgs e)
    {
        Hide();
        var settings = _settingsService.Current;
        settings.ShowOverlayIcon = false;
        _settingsService.Save(settings);
        StateUpdated?.Invoke(this, EventArgs.Empty);
    }
}
