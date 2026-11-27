using System.IO;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class AppDataPaths
{
    public const string ApplicationFolderName = "JellyfinDesktopSlideshow";

    public static AppDataPaths Default { get; } = new();

    public AppDataPaths(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationFolderName)
            : Path.GetFullPath(rootDirectory);

        SettingsFilePath = Path.Combine(RootDirectory, "settings.json");
        CacheRootDirectory = Path.Combine(RootDirectory, "cache");
        BackdropCacheDirectory = Path.Combine(CacheRootDirectory, "backdrops");
        WallpaperStagingDirectory = Path.Combine(CacheRootDirectory, "wallpaper");
        WallpaperStateFilePath = Path.Combine(RootDirectory, "wallpaper-state.json");
        CurrentWallpaperItemFilePath = Path.Combine(RootDirectory, "current-wallpaper-item.json");
        BackupDirectory = Path.Combine(RootDirectory, "backup");
        IconsDirectory = Path.Combine(RootDirectory, "icons");
        DesktopShortcutIconFilePath = Path.Combine(IconsDirectory, "jellyfin-desktop.ico");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }
    public string SettingsFilePath { get; }
    public string CacheRootDirectory { get; }
    public string BackdropCacheDirectory { get; }
    public string WallpaperStagingDirectory { get; }
    public string WallpaperStateFilePath { get; }
    public string CurrentWallpaperItemFilePath { get; }
    public string BackupDirectory { get; }
    public string IconsDirectory { get; }
    public string DesktopShortcutIconFilePath { get; }
    public string LogsDirectory { get; }

    public static string DefaultExternalLanguagesDirectory
        => Path.Combine(AppContext.BaseDirectory, "languages");
}
