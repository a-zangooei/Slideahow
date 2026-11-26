using System.Windows;
using JellyfinDesktopSlideshow.Infrastructure;

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

        if (result.CanFallbackToBrowser && !string.IsNullOrWhiteSpace(result.FallbackBrowserUrl))
        {
            var answer = MessageBox.Show(
                $"{result.Message}\n\nآیا می‌خواهید این عنوان در مرورگر پیش‌فرض باز شود؟",
                "Jellyfin Desktop Slideshow",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                itemLauncher.OpenValidatedBrowserUrl(result.FallbackBrowserUrl);
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
