using System.Windows;
using System.Windows.Threading;
using JellyfinDesktopSlideshow.Composition;
using JellyfinDesktopSlideshow.Localization;

namespace JellyfinDesktopSlideshow;

public partial class App : Application
{
    private ApplicationBootstrapper? _bootstrapper;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _bootstrapper = new ApplicationBootstrapper(this);
        await _bootstrapper.StartAsync(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _bootstrapper?.Logger?.Error("خطای مدیریت‌نشده در ترد رابط کاربری.", e.Exception);
        var loc = AppLocalization.Current;
        MessageBox.Show(
            loc.GetString("App.UnhandledError"),
            loc.GetString("App.Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning,
            MessageBoxResult.OK,
            AppLocalization.GetMessageBoxOptions());
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _bootstrapper?.Logger?.Error("خطای بحرانی در AppDomain.", e.ExceptionObject as Exception);
    }
}
