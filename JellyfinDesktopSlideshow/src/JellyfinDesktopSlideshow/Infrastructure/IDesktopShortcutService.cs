namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed record DesktopShortcutUpdateResult(
    bool Succeeded,
    bool AlreadyExisted,
    string ShortcutPath,
    string Message);

public interface IDesktopShortcutService
{
    string ShortcutFilePath { get; }
    bool IsShortcutPresent();
    DesktopShortcutUpdateResult CreateOrUpdateShortcut();
    bool SetShortcutEnabled(bool enabled);
}

public interface IDesktopShortcutPlatformAdapter
{
    string GetDesktopDirectory();
    bool ShortcutExists(string shortcutPath);
    void CreateOrUpdateShortcut(
        string shortcutPath,
        string targetExePath,
        string arguments,
        string description,
        string iconPath);
    void DeleteShortcut(string shortcutPath);
    string EnsureDesktopIconFile(string iconPath);
}
