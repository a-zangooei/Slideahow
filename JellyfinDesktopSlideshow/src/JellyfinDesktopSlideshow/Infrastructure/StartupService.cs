using System.Diagnostics;
using Microsoft.Win32;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class WindowsStartupRegistryAdapter : IStartupRegistryAdapter
{
    private const string RunSubKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadRunValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void WriteRunValue(string valueName, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunSubKeyPath, writable: true)
            ?? throw new InvalidOperationException("دسترسی به کلید رجیستری اجرای خودکار امکان‌پذیر نیست.");
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void DeleteRunValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKeyPath, writable: true);
        if (key?.GetValue(valueName) is not null)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

public sealed class StartupService : IStartupService
{
    public const string StartupEntryName = "JellyfinDesktopSlideshow";
    public const string MinimizedArgument = "--minimized";

    private readonly IStartupRegistryAdapter _registryAdapter;
    private readonly ILoggingService _logger;
    private readonly Func<string?> _executablePathProvider;

    public StartupService(
        IStartupRegistryAdapter registryAdapter,
        ILoggingService logger,
        Func<string?>? executablePathProvider = null)
    {
        _registryAdapter = registryAdapter;
        _logger = logger;
        _executablePathProvider = executablePathProvider ?? ResolveCurrentExecutablePath;
    }

    public bool IsStartupEnabled()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(_registryAdapter.ReadRunValue(StartupEntryName));
        }
        catch (Exception ex)
        {
            _logger.Warning($"خواندن وضعیت اجرای خودکار از رجیستری ناموفق بود: {ex.Message}");
            return false;
        }
    }

    public bool SetStartupEnabled(bool enabled, bool startMinimized)
    {
        try
        {
            if (!enabled)
            {
                _registryAdapter.DeleteRunValue(StartupEntryName);
                return true;
            }

            var exePath = _executablePathProvider();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                _logger.Warning("مسیر فایل اجرایی برای ثبت اجرای خودکار شناسایی نشد.");
                return false;
            }

            var command = startMinimized
                ? $"\"{exePath}\" {MinimizedArgument}"
                : $"\"{exePath}\"";

            _registryAdapter.WriteRunValue(StartupEntryName, command);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("به‌روزرسانی تنظیمات اجرای خودکار در رجیستری ویندوز ناموفق بود.", ex);
            return false;
        }
    }

    private static string? ResolveCurrentExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
