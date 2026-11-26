using System.Windows;
using JellyfinDesktopSlideshow.ViewModels;

namespace JellyfinDesktopSlideshow.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void ConnectWithApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.IsBusy)
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
        if (DataContext is not SettingsViewModel vm || vm.IsBusy)
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

    private void SelectFallbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "انتخاب تصویر جایگزین",
            Filter = "تصاویر پشتیبانی‌شده|*.jpg;*.jpeg;*.png;*.webp;*.bmp"
        };

        if (dialog.ShowDialog(this) == true)
        {
            vm.SetFallbackImagePath(dialog.FileName);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
