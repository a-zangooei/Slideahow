using System.Diagnostics;
using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Services;
using Microsoft.Win32;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class WindowsExternalLauncher : IExternalLauncher
{
    private readonly ILoggingService _logger;

    public WindowsExternalLauncher(ILoggingService logger)
    {
        _logger = logger;
    }

    public bool OpenHttpUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.Warning($"تلاش برای باز کردن پروتکل غیرمجاز متوقف شد: {uri.Scheme}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("خطا در باز کردن لینک در مرورگر.", ex);
            return false;
        }
    }

    public bool IsJellyfinDesktopInstalled()
    {
        var candidatePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Jellyfin", "Jellyfin Media Player", "JellyfinMediaPlayer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Jellyfin", "Jellyfin Desktop", "JellyfinDesktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Jellyfin", "Jellyfin Media Player", "JellyfinMediaPlayer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Jellyfin", "Jellyfin Media Player", "JellyfinMediaPlayer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JellyfinMediaPlayer", "JellyfinMediaPlayer.exe")
        };

        if (candidatePaths.Any(File.Exists))
        {
            return true;
        }

        return HasUninstallEntry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall") ||
               HasUninstallEntry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall") ||
               HasUninstallEntry(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
    }

    private static bool HasUninstallEntry(RegistryKey root, string subKeyPath)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key is null)
            {
                return false;
            }

            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                var displayName = sub?.GetValue("DisplayName") as string;
                if (!string.IsNullOrWhiteSpace(displayName) &&
                    (displayName.Contains("Jellyfin Media Player", StringComparison.OrdinalIgnoreCase) ||
                     displayName.Contains("Jellyfin Desktop", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore restricted registry branches.
        }

        return false;
    }
}
