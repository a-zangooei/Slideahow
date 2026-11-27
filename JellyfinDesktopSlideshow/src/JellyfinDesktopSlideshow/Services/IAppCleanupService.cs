namespace JellyfinDesktopSlideshow.Services;

public sealed class AppCleanupFootprint
{
    public required string RootDirectory { get; init; }
    public bool RootDirectoryExists { get; init; }
    public int BackdropCacheFiles { get; init; }
    public long BackdropCacheBytes { get; init; }
    public int WallpaperStagingFiles { get; init; }
    public long WallpaperStagingBytes { get; init; }
    public int LogFiles { get; init; }
    public long LogBytes { get; init; }
    public int BackupFiles { get; init; }
    public long BackupBytes { get; init; }
    public bool HasSettingsFile { get; init; }
    public bool HasWallpaperStateFile { get; init; }
    public bool HasCurrentItemFile { get; init; }
    public bool HasIconFile { get; init; }
    public bool HasSavedCredential { get; init; }
    public bool HasStartupRegistryEntry { get; init; }
    public bool HasDesktopShortcut { get; init; }

    public long TotalCacheBytes => BackdropCacheBytes + WallpaperStagingBytes;

    public bool HasAnyFootprint =>
        RootDirectoryExists ||
        BackdropCacheFiles > 0 ||
        WallpaperStagingFiles > 0 ||
        LogFiles > 0 ||
        BackupFiles > 0 ||
        HasSettingsFile ||
        HasWallpaperStateFile ||
        HasCurrentItemFile ||
        HasIconFile ||
        HasSavedCredential ||
        HasStartupRegistryEntry ||
        HasDesktopShortcut;
}

public sealed class AppCleanupOptions
{
    public bool ClearBackdropCache { get; init; }
    public bool ClearWallpaperStagingCache { get; init; }
    public bool ClearLogsAndHiddenFiles { get; init; }
    public bool RemoveCredentialsAndSession { get; init; }
    public bool RemoveOsTraces { get; init; }
    public bool RestoreOriginalWallpaper { get; init; } = true;
    public bool DeleteSettingsAndReset { get; init; }

    public bool HasAnyOptionSelected =>
        ClearBackdropCache ||
        ClearWallpaperStagingCache ||
        ClearLogsAndHiddenFiles ||
        RemoveCredentialsAndSession ||
        RemoveOsTraces ||
        DeleteSettingsAndReset;

    public static AppCleanupOptions FullWipe(bool restoreOriginalWallpaper = true) => new()
    {
        ClearBackdropCache = true,
        ClearWallpaperStagingCache = true,
        ClearLogsAndHiddenFiles = true,
        RemoveCredentialsAndSession = true,
        RemoveOsTraces = true,
        RestoreOriginalWallpaper = restoreOriginalWallpaper,
        DeleteSettingsAndReset = true
    };
}

public sealed record AppCleanupResult(
    bool Succeeded,
    int DeletedFilesCount,
    long FreedBytes,
    bool RootDirectoryRemoved,
    IReadOnlyList<string> Warnings,
    string Message);

public interface IAppCleanupService
{
    AppCleanupFootprint GetFootprint();
    Task<AppCleanupResult> CleanAsync(AppCleanupOptions options, CancellationToken cancellationToken = default);
}
