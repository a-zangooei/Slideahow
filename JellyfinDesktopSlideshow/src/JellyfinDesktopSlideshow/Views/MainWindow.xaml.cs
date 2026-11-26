using System.ComponentModel;
using System.Windows;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;

namespace JellyfinDesktopSlideshow.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel, ISettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RefreshDisplayConfiguration();
            }
        });
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_settingsService.Current.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private async void ConnectWithApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy)
        {
            return;
        }

        var apiKey = ApiKeyInput.Password;
        var succeeded = await vm.LoginWithApiKeyAsync(apiKey);
        if (succeeded)
        {
            ApiKeyInput.Clear();
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy)
        {
            return;
        }

        var password = PasswordInput.Password;
        var succeeded = await vm.LoginAsync(password);
        if (succeeded)
        {
            PasswordInput.Clear();
        }
    }
}
