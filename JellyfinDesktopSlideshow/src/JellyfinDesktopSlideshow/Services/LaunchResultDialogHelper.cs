using System.Windows;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;

namespace JellyfinDesktopSlideshow.Services;

internal static class LaunchResultDialogHelper
{
    public static void HandleFailure(
        ItemLaunchResult result,
        IItemLauncher itemLauncher,
        ILoggingService? logger,
        string sourceLabel)
    {
        if (result.Succeeded)
        {
            return;
        }

        logger?.Warning($"باز کردن عنوان از {sourceLabel} ناموفق بود: {result.Message}");
        var loc = AppLocalization.Current;
        var title = loc.GetString("App.Title");
        var options = AppLocalization.GetMessageBoxOptions();

        if (result.CanFallbackToBrowser && !string.IsNullOrWhiteSpace(result.FallbackBrowserUrl))
        {
            var answer = MessageBox.Show(
                loc.Format("Launch.Dialog.FallbackBrowserPromptFormat", result.Message),
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.No,
                options);

            if (answer == MessageBoxResult.Yes)
            {
                itemLauncher.OpenValidatedBrowserUrl(result.FallbackBrowserUrl);
            }
        }
        else
        {
            MessageBox.Show(
                result.Message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK,
                options);
        }
    }
}
