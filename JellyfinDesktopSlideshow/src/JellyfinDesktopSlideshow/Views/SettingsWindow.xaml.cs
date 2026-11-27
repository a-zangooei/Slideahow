using System.Windows;
using JellyfinDesktopSlideshow.Localization;
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

        var loc = AppLocalization.Current;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = loc.GetString("Settings.Tab6.SelectFallbackDialogTitle"),
            Filter = loc.GetString("Settings.Tab6.SelectFallbackDialogFilter")
        };

        if (dialog.ShowDialog(this) == true)
        {
            vm.SetFallbackImagePath(dialog.FileName);
        }
    }

    private async void CleanSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.IsBusy)
        {
            return;
        }

        if (vm.CleanResetSettingsFile)
        {
            var loc = AppLocalization.Current;
            var confirm = MessageBox.Show(
                this,
                loc.GetString("Settings.Tab8.ConfirmSelectedResetMessage"),
                loc.GetString("App.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                AppLocalization.GetMessageBoxOptions());

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await vm.ExecuteSelectedCleanupAsync();
    }

    private async void FullWipeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.IsBusy)
        {
            return;
        }

        var loc = AppLocalization.Current;
        var confirm = MessageBox.Show(
            this,
            loc.GetString("Settings.Tab8.ConfirmFullWipeMessage"),
            loc.GetString("App.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No,
            AppLocalization.GetMessageBoxOptions());

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await vm.ExecuteFullWipeAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
