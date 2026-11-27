using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;

namespace JellyfinDesktopSlideshow.Services;

public sealed class AppCleanupService : IAppCleanupService
{
    private readonly AppDataPaths _paths;
    private readonly ICacheService _cacheService;
    private readonly IWallpaperService _wallpaperService;
    private readonly ISlideshowService _slideshowService;
    private readonly IAuthenticationService _authService;
    private readonly ISecureStorageService _secureStorage;
    private readonly IStartupService _startupService;
    private readonly IDesktopShortcutService _desktopShortcutService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly IOverlayService? _overlayService;
    private readonly ICurrentWallpaperItemStore? _currentItemStore;
    private readonly ILocalizationService _localization;

    public AppCleanupService(
        AppDataPaths paths,
        ICacheService cacheService,
        IWallpaperService wallpaperService,
        ISlideshowService slideshowService,
        IAuthenticationService authService,
        ISecureStorageService secureStorage,
        IStartupService startupService,
        IDesktopShortcutService desktopShortcutService,
        ISettingsService settingsService,
        ILoggingService logger,
        IOverlayService? overlayService = null,
        ICurrentWallpaperItemStore? currentItemStore = null,
        ILocalizationService? localization = null)
    {
        _paths = paths;
        _cacheService = cacheService;
        _wallpaperService = wallpaperService;
        _slideshowService = slideshowService;
        _authService = authService;
        _secureStorage = secureStorage;
        _startupService = startupService;
        _desktopShortcutService = desktopShortcutService;
        _settingsService = settingsService;
        _logger = logger;
        _overlayService = overlayService;
        _currentItemStore = currentItemStore;
        _localization = localization ?? AppLocalization.Current;
    }

    public AppCleanupFootprint GetFootprint()
    {
        var (backdropFiles, backdropBytes) = MeasureDirectory(_cacheService.CacheDirectory);
        var (stagingFiles, stagingBytes) = MeasureDirectory(_paths.WallpaperStagingDirectory);
        var (logFiles, logBytes) = MeasureDirectory(_paths.LogsDirectory);
        var (backupFiles, backupBytes) = MeasureDirectory(_paths.BackupDirectory);

        var credentialKey = _settingsService.Current.TokenCredentialKey;
        var hasSavedCredential = false;
        if (!string.IsNullOrWhiteSpace(credentialKey))
        {
            try
            {
                hasSavedCredential = !string.IsNullOrWhiteSpace(_secureStorage.ReadSecret(credentialKey));
            }
            catch
            {
                hasSavedCredential = false;
            }
        }

        return new AppCleanupFootprint
        {
            RootDirectory = _paths.RootDirectory,
            RootDirectoryExists = Directory.Exists(_paths.RootDirectory),
            BackdropCacheFiles = backdropFiles,
            BackdropCacheBytes = backdropBytes,
            WallpaperStagingFiles = stagingFiles,
            WallpaperStagingBytes = stagingBytes,
            LogFiles = logFiles,
            LogBytes = logBytes,
            BackupFiles = backupFiles,
            BackupBytes = backupBytes,
            HasSettingsFile = File.Exists(_settingsService.SettingsFilePath) || File.Exists(_paths.SettingsFilePath),
            HasWallpaperStateFile = File.Exists(_paths.WallpaperStateFilePath),
            HasCurrentItemFile = File.Exists(_paths.CurrentWallpaperItemFilePath),
            HasIconFile = File.Exists(_paths.DesktopShortcutIconFilePath),
            HasSavedCredential = hasSavedCredential,
            HasStartupRegistryEntry = _startupService.IsStartupEnabled(),
            HasDesktopShortcut = _desktopShortcutService.IsShortcutPresent()
        };
    }

    public async Task<AppCleanupResult> CleanAsync(AppCleanupOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.HasAnyOptionSelected)
        {
            return new AppCleanupResult(
                true,
                0,
                0,
                !Directory.Exists(_paths.RootDirectory),
                [],
                _localization.GetString("Settings.Tab8.NothingSelected"));
        }

        var warnings = new List<string>();
        var deletedFiles = 0;
        long freedBytes = 0;

        // Log only before we potentially delete the logs directory so logs/ is never recreated afterward.
        if (!options.ClearLogsAndHiddenFiles)
        {
            _logger.Info("شروع عملیات پاک‌سازی انتخابی داده‌های برنامه.");
        }

        var shouldStopSlideshow = options.RemoveCredentialsAndSession ||
                                  options.DeleteSettingsAndReset ||
                                  options.RemoveOsTraces ||
                                  options.ClearLogsAndHiddenFiles;

        if (shouldStopSlideshow)
        {
            _slideshowService.Stop();
        }

        // 1. Restore original wallpaper BEFORE removing wallpaper-state.json & backup/
        if (options.RemoveOsTraces && options.RestoreOriginalWallpaper && _wallpaperService.HasCapturedOriginalWallpaper)
        {
            try
            {
                var restoreResult = await _wallpaperService.RestoreOriginalWallpaperAsync(cancellationToken);
                if (!restoreResult.Succeeded && !string.IsNullOrWhiteSpace(restoreResult.Message))
                {
                    warnings.Add(restoreResult.Message);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add(ex.Message);
            }
        }

        // 2. Backdrop image cache
        if (options.ClearBackdropCache)
        {
            var statsBefore = _cacheService.GetStats();
            _cacheService.ClearCache();
            var (stagingTmpCount, stagingTmpBytes) = DeleteDirectoryContents(_cacheService.CacheDirectory, warnings);
            deletedFiles += statsBefore.FileCount + stagingTmpCount;
            freedBytes += statsBefore.TotalBytes + stagingTmpBytes;

            if (!PathsEqual(_cacheService.CacheDirectory, _paths.BackdropCacheDirectory))
            {
                var (extraCount, extraBytes) = DeleteDirectoryContents(_paths.BackdropCacheDirectory, warnings);
                deletedFiles += extraCount;
                freedBytes += extraBytes;
            }
        }

        // 3. Wallpaper staging cache (converted desktop_wallpaper.png + temp files)
        if (options.ClearWallpaperStagingCache)
        {
            var (count, bytes) = DeleteDirectoryContents(_paths.WallpaperStagingDirectory, warnings);
            deletedFiles += count;
            freedBytes += bytes;
        }

        // 4. Credentials & active session
        var credentialKey = _settingsService.Current.TokenCredentialKey;
        if (options.RemoveCredentialsAndSession)
        {
            try
            {
                await _authService.LogoutAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add(ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(credentialKey))
            {
                try
                {
                    _secureStorage.DeleteSecret(credentialKey);
                }
                catch (Exception ex)
                {
                    warnings.Add(ex.Message);
                }
            }
        }

        // 5. OS traces: startup registry, desktop shortcut, floating overlay, wallpaper state & backup
        if (options.RemoveOsTraces)
        {
            if (!_startupService.SetStartupEnabled(false, false))
            {
                warnings.Add(_localization.GetString("Settings.Status.StartupToggleError"));
            }

            if (!_desktopShortcutService.SetShortcutEnabled(false))
            {
                warnings.Add(_localization.GetString("Settings.Status.ShortcutToggleError"));
            }

            _overlayService?.Hide();

            var (wsCount, wsBytes) = DeleteFileAndTemps(_paths.WallpaperStateFilePath, warnings);
            deletedFiles += wsCount;
            freedBytes += wsBytes;

            var (backupCount, backupBytes) = DeleteDirectoryContents(_paths.BackupDirectory, warnings, removeDirectory: true);
            deletedFiles += backupCount;
            freedBytes += backupBytes;
        }

        // 6. Hidden files & logs: current item store, icon cache, logs, stray .tmp files
        if (options.ClearLogsAndHiddenFiles)
        {
            _currentItemStore?.Clear();
            var (curCount, curBytes) = DeleteFileAndTemps(_paths.CurrentWallpaperItemFilePath, warnings);
            deletedFiles += curCount;
            freedBytes += curBytes;

            var (iconCount, iconBytes) = DeleteDirectoryContents(_paths.IconsDirectory, warnings, removeDirectory: true);
            deletedFiles += iconCount;
            freedBytes += iconBytes;

            var (logCount, logBytes) = DeleteDirectoryContents(_paths.LogsDirectory, warnings, removeDirectory: true);
            deletedFiles += logCount;
            freedBytes += logBytes;

            var (tmpCount, tmpBytes) = DeleteTopLevelTempFiles(_paths.RootDirectory, warnings);
            deletedFiles += tmpCount;
            freedBytes += tmpBytes;
        }

        // 7. Settings file reset OR keep settings aligned with cleared traces
        if (options.DeleteSettingsAndReset)
        {
            var (setCount, setBytes) = DeleteFileAndTemps(_settingsService.SettingsFilePath, warnings);
            deletedFiles += setCount;
            freedBytes += setBytes;

            if (!PathsEqual(_settingsService.SettingsFilePath, _paths.SettingsFilePath))
            {
                var (rootSetCount, rootSetBytes) = DeleteFileAndTemps(_paths.SettingsFilePath, warnings);
                deletedFiles += rootSetCount;
                freedBytes += rootSetBytes;
            }

            _settingsService.ResetInMemoryToDefaults();
        }
        else if (options.RemoveCredentialsAndSession || options.RemoveOsTraces)
        {
            var settings = _settingsService.Current;
            if (options.RemoveCredentialsAndSession)
            {
                settings.ServerId = null;
                settings.UserId = null;
                settings.Username = null;
                settings.SelectedLibraryIds = [];
            }

            if (options.RemoveOsTraces)
            {
                settings.LaunchOnWindowsStartup = false;
                settings.ShowDesktopShortcutIcon = false;
                settings.ShowOverlayIcon = false;
            }

            _settingsService.Save(settings);
        }

        // 8. Prune empty folders & remove RootDirectory cleanly if zero files remain
        TryDeleteDirectoryIfEmpty(_paths.BackdropCacheDirectory);
        TryDeleteDirectoryIfEmpty(_paths.WallpaperStagingDirectory);
        TryDeleteDirectoryIfEmpty(_paths.CacheRootDirectory);
        TryDeleteDirectoryIfEmpty(_paths.BackupDirectory);
        TryDeleteDirectoryIfEmpty(_paths.IconsDirectory);
        TryDeleteDirectoryIfEmpty(_paths.LogsDirectory);
        var rootRemoved = TryDeleteDirectoryIfEmpty(_paths.RootDirectory);

        var freedMb = freedBytes / (1024.0 * 1024.0);
        string message;
        if (warnings.Count > 0)
        {
            message = _localization.Format("Settings.Tab8.CleanupDoneWithErrors", string.Join(" | ", warnings));
        }
        else if (options.DeleteSettingsAndReset && options.RemoveOsTraces && options.ClearLogsAndHiddenFiles)
        {
            message = rootRemoved
                ? $"{_localization.GetString("Settings.Tab8.FullWipeCompleted")} {_localization.GetString("Settings.Tab8.FullWipeRootRemoved")}"
                : _localization.GetString("Settings.Tab8.FullWipeCompleted");
        }
        else
        {
            message = _localization.Format("Settings.Tab8.CleanupDoneSummary", deletedFiles, freedMb);
        }

        return new AppCleanupResult(
            warnings.Count == 0,
            deletedFiles,
            freedBytes,
            rootRemoved,
            warnings,
            message);
    }

    private static (int FileCount, long TotalBytes) MeasureDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return (0, 0);
            }

            var files = new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories);
            return (files.Length, files.Sum(f => f.Length));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (int DeletedCount, long FreedBytes) DeleteDirectoryContents(
        string directory,
        List<string> warnings,
        bool removeDirectory = false)
    {
        var count = 0;
        long bytes = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return (0, 0);
            }

            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (TryDeleteFile(file, warnings, out var len))
                {
                    count++;
                    bytes += len;
                }
            }

            if (removeDirectory)
            {
                TryDeleteDirectoryTree(directory);
            }
        }
        catch (Exception ex)
        {
            warnings.Add(ex.Message);
        }

        return (count, bytes);
    }

    private static (int DeletedCount, long FreedBytes) DeleteFileAndTemps(string filePath, List<string> warnings)
    {
        var count = 0;
        long bytes = 0;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return (0, 0);
        }

        if (File.Exists(filePath) && TryDeleteFile(filePath, warnings, out var len))
        {
            count++;
            bytes += len;
        }

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(fileName) && Directory.Exists(dir))
            {
                foreach (var tmp in Directory.GetFiles(dir, $"{fileName}.*tmp"))
                {
                    if (TryDeleteFile(tmp, warnings, out var tmpLen))
                    {
                        count++;
                        bytes += tmpLen;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(ex.Message);
        }

        return (count, bytes);
    }

    private static (int DeletedCount, long FreedBytes) DeleteTopLevelTempFiles(string rootDirectory, List<string> warnings)
    {
        var count = 0;
        long bytes = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                return (0, 0);
            }

            foreach (var tmp in Directory.GetFiles(rootDirectory, "*.tmp", SearchOption.AllDirectories))
            {
                if (TryDeleteFile(tmp, warnings, out var len))
                {
                    count++;
                    bytes += len;
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(ex.Message);
        }

        return (count, bytes);
    }

    private static bool TryDeleteFile(string path, List<string> warnings, out long length)
    {
        length = 0;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var info = new FileInfo(path);
            if (info.IsReadOnly)
            {
                info.IsReadOnly = false;
            }

            length = info.Length;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteDirectoryTree(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch
        {
            // Non-fatal cleanup
        }
    }

    private static bool TryDeleteDirectoryIfEmpty(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return true;
            }

            if (!Directory.Exists(directory))
            {
                return true;
            }

            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return false;
            }

            Directory.Delete(directory, recursive: false);
            return !Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
