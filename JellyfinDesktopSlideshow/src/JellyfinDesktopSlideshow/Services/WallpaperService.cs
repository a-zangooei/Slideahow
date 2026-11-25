using System.IO;
using System.Text.Json;
using JellyfinDesktopSlideshow.Infrastructure;

namespace JellyfinDesktopSlideshow.Services;

public sealed class WallpaperBackupState
{
    public bool HasActiveCapture { get; set; }
    public string? OriginalWallpaperPath { get; set; }
    public string? OriginalWallpaperStyle { get; set; }
    public string? OriginalTileWallpaper { get; set; }
    public WindowsBackgroundMode OriginalBackgroundMode { get; set; } = WindowsBackgroundMode.Unknown;
    public string? BackupImagePath { get; set; }
    public string? LastAppliedByAppPath { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}

public sealed class WallpaperService : IWallpaperService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IWallpaperPlatformAdapter _platformAdapter;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly string _stagingDirectory;
    private readonly string _managedCacheDirectory;
    private readonly string _stateFilePath;
    private readonly string _backupDirectory;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Lock _stateLock = new();

    private WallpaperBackupState _state = new();
    private bool _stateLoadedFromDisk;
    private bool _verifiedCurrentProcessSession;

    public string? LastAppliedWallpaperPath { get; private set; }

    public bool HasCapturedOriginalWallpaper
    {
        get
        {
            EnsureStateLoaded();
            lock (_stateLock)
            {
                return _state.HasActiveCapture;
            }
        }
    }

    public WallpaperService(
        IWallpaperPlatformAdapter platformAdapter,
        ISettingsService settingsService,
        ILoggingService logger,
        string? stagingDirectory = null,
        string? stateFilePath = null,
        string? managedCacheDirectory = null)
    {
        _platformAdapter = platformAdapter;
        _settingsService = settingsService;
        _logger = logger;

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JellyfinDesktopSlideshow");

        _stagingDirectory = stagingDirectory ?? Path.Combine(appDataRoot, "cache", "wallpaper");
        _managedCacheDirectory = managedCacheDirectory ?? Path.Combine(appDataRoot, "cache", "backdrops");
        _stateFilePath = stateFilePath ?? Path.Combine(appDataRoot, "wallpaper-state.json");

        var stateParent = Path.GetDirectoryName(_stateFilePath);
        _backupDirectory = string.IsNullOrWhiteSpace(stateParent)
            ? Path.Combine(appDataRoot, "backup")
            : Path.Combine(stateParent, "backup");
    }

    public async Task<WallpaperApplyResult> ApplyWallpaperAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !ImageFormatValidator.IsValidImageFile(imagePath))
        {
            return new WallpaperApplyResult(false, "فایل تصویر برای تنظیم والپیپر معتبر نیست یا وجود ندارد.");
        }

        await _applyGate.WaitAsync(cancellationToken);
        try
        {
            EnsureStateLoaded();

            var capturedNewSnapshotOnThisCall = false;
            var currentCapture = GetStateSnapshotCopy();

            if (!currentCapture.HasActiveCapture)
            {
                var osSnapshot = _platformAdapter.GetCurrentDesktopWallpaperState();
                if (IsAppManagedPath(osSnapshot.WallpaperPath, currentCapture.LastAppliedByAppPath))
                {
                    _logger.Warning("والپیپر فعلی ویندوز متعلق به فایل‌های داخلی همین برنامه است؛ به‌عنوان وضعیت اولیهٔ کاربر ذخیره نشد.");
                }
                else
                {
                    CaptureAndPersistOriginalSnapshot(osSnapshot);
                    capturedNewSnapshotOnThisCall = true;
                }

                _verifiedCurrentProcessSession = true;
            }
            else if (!_verifiedCurrentProcessSession)
            {
                // Loaded an unrestored snapshot from a previous run (crash or exit with RestoreWallpaperOnExit=false).
                // Only replace it if the user manually picked a different non-app wallpaper while the app was closed.
                var osSnapshot = _platformAdapter.GetCurrentDesktopWallpaperState();
                if (HasExternalManualWallpaperChange(osSnapshot, currentCapture))
                {
                    _logger.Info("تغییر دستی والپیپر ویندوز خارج از برنامه تشخیص داده شد؛ وضعیت فعلی به‌عنوان وضعیت اولیهٔ جدید ذخیره می‌شود.");
                    DeleteBackupFileIfExists(currentCapture.BackupImagePath);
                    CaptureAndPersistOriginalSnapshot(osSnapshot);
                    capturedNewSnapshotOnThisCall = true;
                }

                _verifiedCurrentProcessSession = true;
            }

            var fitMode = _settingsService.Current.WallpaperFitMode;
            var compatiblePath = await Task.Run(
                () => _platformAdapter.EnsureCompatibleImageFormat(imagePath, _stagingDirectory),
                cancellationToken);

            var result = await Task.Run(
                () => _platformAdapter.SetDesktopWallpaper(compatiblePath, fitMode),
                cancellationToken);

            if (result.Succeeded)
            {
                UpdateLastAppliedByAppPath(compatiblePath);
                LastAppliedWallpaperPath = compatiblePath;
                return new WallpaperApplyResult(true, result.Message, compatiblePath, 0);
            }

            if (capturedNewSnapshotOnThisCall && string.IsNullOrWhiteSpace(GetStateSnapshotCopy().LastAppliedByAppPath))
            {
                // Roll back the fresh capture because the OS wallpaper was never actually modified.
                ClearCapturedState();
            }

            _logger.Warning($"خطای API ویندوز هنگام تغییر والپیپر: {result.Message}");
            return new WallpaperApplyResult(false, result.Message, null, result.Win32ErrorCode);
        }
        catch (OperationCanceledException)
        {
            return new WallpaperApplyResult(false, "تغییر والپیپر لغو شد.");
        }
        catch (Exception ex)
        {
            _logger.Error("خطای غیرمنتظره در WallpaperService.", ex);
            return new WallpaperApplyResult(false, $"تنظیم والپیپر با خطا مواجه شد: {ex.Message}");
        }
        finally
        {
            _applyGate.Release();
        }
    }

    public Task<WallpaperApplyResult> ReapplyCurrentWallpaperAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(LastAppliedWallpaperPath))
        {
            return Task.FromResult(new WallpaperApplyResult(false, "هنوز والپیپری تنظیم نشده است."));
        }

        return ApplyWallpaperAsync(LastAppliedWallpaperPath, cancellationToken);
    }

    public async Task<WallpaperApplyResult> RestoreOriginalWallpaperAsync(CancellationToken cancellationToken = default)
    {
        await _applyGate.WaitAsync(cancellationToken);
        try
        {
            EnsureStateLoaded();
            var snapshot = GetStateSnapshotCopy();
            if (!snapshot.HasActiveCapture)
            {
                LastAppliedWallpaperPath = null;
                return new WallpaperApplyResult(false, "والپیپر اولیه برای بازگردانی در دسترس نیست.");
            }

            if (!_verifiedCurrentProcessSession)
            {
                var osSnapshot = _platformAdapter.GetCurrentDesktopWallpaperState();
                if (HasExternalManualWallpaperChange(osSnapshot, snapshot))
                {
                    _logger.Info("پس‌زمینهٔ ویندوز پیش از بازگردانی به‌صورت دستی توسط کاربر تغییر یافته بود؛ بازگردانی خودکار انجام نشد.");
                    ClearCapturedState();
                    LastAppliedWallpaperPath = null;
                    _verifiedCurrentProcessSession = false;
                    return new WallpaperApplyResult(
                        false,
                        "والپیپر ویندوز پس از آخرین اجرای برنامه به‌صورت دستی تغییر کرده است؛ برای حفظ انتخاب فعلی شما بازگردانی انجام نشد.");
                }
            }

            // Solid Color mode (or empty wallpaper path) cannot be reliably restored via SPI_SETDESKWALLPAPER:
            // passing "" to SPI_SETDESKWALLPAPER on Windows 10/11 often turns the desktop black instead of restoring
            // the original solid color. Avoid destructive fallback and report the limitation clearly.
            if (snapshot.OriginalBackgroundMode == WindowsBackgroundMode.SolidColor ||
                string.IsNullOrWhiteSpace(snapshot.OriginalWallpaperPath))
            {
                _logger.Warning("بازگردانی کامل پس‌زمینهٔ Solid Color از طریق SystemParametersInfoW پشتیبانی نمی‌شود.");
                ClearCapturedState();
                LastAppliedWallpaperPath = null;
                _verifiedCurrentProcessSession = false;
                return new WallpaperApplyResult(
                    false,
                    "پس‌زمینهٔ قبلی ویندوز «رنگ یکدست (Solid Color)» بوده و از طریق API فعلی ویندوز به‌صورت خودکار قابل بازگردانی نیست.");
            }

            string? restoreSourcePath;
            if (IsWindowsVolatileThemePath(snapshot.OriginalWallpaperPath))
            {
                // Windows overwrites %AppData%\Microsoft\Windows\Themes\TranscodedWallpaper whenever
                // SPI_SETDESKWALLPAPER runs, so restore from the pre-change backup copy.
                if (string.IsNullOrWhiteSpace(snapshot.BackupImagePath) || !File.Exists(snapshot.BackupImagePath))
                {
                    _logger.Warning("نسخهٔ پشتیبان فایل موقت TranscodedWallpaper در دسترس نیست.");
                    ClearCapturedState();
                    LastAppliedWallpaperPath = null;
                    _verifiedCurrentProcessSession = false;
                    return new WallpaperApplyResult(
                        false,
                        "فایل تصویر موقت قبلی ویندوز (TranscodedWallpaper) قابل بازیابی نیست.");
                }

                restoreSourcePath = snapshot.BackupImagePath;
            }
            else
            {
                if (!File.Exists(snapshot.OriginalWallpaperPath))
                {
                    _logger.Warning($"فایل والپیپر قبلی کاربر در مسیر '{snapshot.OriginalWallpaperPath}' یافت نشد.");
                    ClearCapturedState();
                    LastAppliedWallpaperPath = null;
                    _verifiedCurrentProcessSession = false;
                    return new WallpaperApplyResult(
                        false,
                        "فایل تصویر پس‌زمینهٔ قبلی دیگر در مسیر اصلی وجود ندارد و قابل بازگردانی نیست.");
                }

                restoreSourcePath = snapshot.OriginalWallpaperPath;
            }

            var compatibleRestorePath = await Task.Run(
                () => _platformAdapter.EnsureCompatibleImageFormat(restoreSourcePath, _stagingDirectory),
                cancellationToken);

            var result = await Task.Run(
                () => _platformAdapter.RestoreDesktopWallpaper(
                    compatibleRestorePath,
                    snapshot.OriginalWallpaperStyle,
                    snapshot.OriginalTileWallpaper),
                cancellationToken);

            if (!result.Succeeded)
            {
                _logger.Warning($"بازگردانی والپیپر قبلی ویندوز ناموفق بود: {result.Message}");
                return new WallpaperApplyResult(false, result.Message, null, result.Win32ErrorCode);
            }

            var originalMode = snapshot.OriginalBackgroundMode;
            var keepRestoredBackupFile = PathsEqual(compatibleRestorePath, snapshot.BackupImagePath);
            ClearCapturedState(deleteBackupFile: !keepRestoredBackupFile);
            LastAppliedWallpaperPath = null;
            _verifiedCurrentProcessSession = false;

            if (originalMode == WindowsBackgroundMode.Slideshow)
            {
                _logger.Warning("تصویر و چینش پیش از شروع برنامه بازگردانی شد، اما چرخش خودکار Windows Slideshow از طریق SystemParametersInfoW قابل فعال‌سازی خودکار نیست.");
                return new WallpaperApplyResult(
                    true,
                    "تصویر و چینش قبلی بازگردانی شد؛ اما چرخش خودکار Windows Slideshow از طریق API فعلی قابل فعال‌سازی خودکار نیست.",
                    compatibleRestorePath,
                    0);
            }

            if (originalMode == WindowsBackgroundMode.Spotlight)
            {
                _logger.Warning("آخرین تصویر و چینش پیش از شروع برنامه بازگردانی شد، اما سرویس پویا (Windows Spotlight) از طریق SystemParametersInfoW قابل فعال‌سازی خودکار نیست.");
                return new WallpaperApplyResult(
                    true,
                    "آخرین تصویر و چینش قبلی بازگردانی شد؛ اما سرویس پویا (Windows Spotlight) از طریق API فعلی قابل فعال‌سازی خودکار نیست.",
                    compatibleRestorePath,
                    0);
            }

            return new WallpaperApplyResult(
                true,
                "والپیپر و تنظیمات چینش قبلی ویندوز با موفقیت بازگردانی شد.",
                compatibleRestorePath,
                0);
        }
        catch (OperationCanceledException)
        {
            return new WallpaperApplyResult(false, "بازگردانی والپیپر لغو شد.");
        }
        catch (Exception ex)
        {
            _logger.Error("خطای غیرمنتظره هنگام بازگردانی والپیپر قبلی.", ex);
            return new WallpaperApplyResult(false, $"بازگردانی والپیپر با خطا مواجه شد: {ex.Message}");
        }
        finally
        {
            _applyGate.Release();
        }
    }

    public string GetMonitorsSummary()
    {
        try
        {
            var monitors = _platformAdapter.GetMonitors();
            if (monitors.Count == 0)
            {
                return "اطلاعات نمایشگر در دسترس نیست.";
            }

            var parts = monitors.Select((m, idx) =>
            {
                var label = m.IsPrimary ? $"مانیتور {idx + 1} (اصلی)" : $"مانیتور {idx + 1}";
                return $"{label}: {m.Width}×{m.Height}";
            });

            return string.Join(" | ", parts);
        }
        catch (Exception ex)
        {
            _logger.Warning($"خواندن رزولوشن مانیتورها ناموفق بود: {ex.Message}");
            return "خطا در خواندن رزولوشن مانیتورها.";
        }
    }

    private void EnsureStateLoaded()
    {
        lock (_stateLock)
        {
            if (_stateLoadedFromDisk)
            {
                return;
            }

            _stateLoadedFromDisk = true;
            try
            {
                if (!File.Exists(_stateFilePath))
                {
                    _state = new WallpaperBackupState();
                    return;
                }

                var json = File.ReadAllText(_stateFilePath);
                var loaded = JsonSerializer.Deserialize<WallpaperBackupState>(json, JsonOptions);
                _state = loaded ?? new WallpaperBackupState();
            }
            catch (Exception ex)
            {
                _logger.Warning($"خواندن وضعیت ذخیره‌شدهٔ والپیپر ناموفق بود: {ex.Message}");
                _state = new WallpaperBackupState();
            }
        }
    }

    private WallpaperBackupState GetStateSnapshotCopy()
    {
        lock (_stateLock)
        {
            return new WallpaperBackupState
            {
                HasActiveCapture = _state.HasActiveCapture,
                OriginalWallpaperPath = _state.OriginalWallpaperPath,
                OriginalWallpaperStyle = _state.OriginalWallpaperStyle,
                OriginalTileWallpaper = _state.OriginalTileWallpaper,
                OriginalBackgroundMode = _state.OriginalBackgroundMode,
                BackupImagePath = _state.BackupImagePath,
                LastAppliedByAppPath = _state.LastAppliedByAppPath,
                CapturedAtUtc = _state.CapturedAtUtc
            };
        }
    }

    private void CaptureAndPersistOriginalSnapshot(DesktopWallpaperSnapshot osSnapshot)
    {
        var backupImagePath = TryCreateVolatileBackup(osSnapshot.WallpaperPath);
        var nextState = new WallpaperBackupState
        {
            HasActiveCapture = true,
            OriginalWallpaperPath = osSnapshot.WallpaperPath,
            OriginalWallpaperStyle = osSnapshot.WallpaperStyle,
            OriginalTileWallpaper = osSnapshot.TileWallpaper,
            OriginalBackgroundMode = osSnapshot.BackgroundMode,
            BackupImagePath = backupImagePath,
            LastAppliedByAppPath = null,
            CapturedAtUtc = DateTime.UtcNow
        };

        lock (_stateLock)
        {
            _state = nextState;
            PersistStateLocked();
        }
    }

    private void UpdateLastAppliedByAppPath(string appliedPath)
    {
        lock (_stateLock)
        {
            _state.LastAppliedByAppPath = appliedPath;
            PersistStateLocked();
        }
    }

    private void ClearCapturedState(bool deleteBackupFile = true)
    {
        string? backupToDelete;
        lock (_stateLock)
        {
            backupToDelete = _state.BackupImagePath;
            _state = new WallpaperBackupState();
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    File.Delete(_stateFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"پاک‌سازی فایل وضعیت والپیپر ناموفق بود: {ex.Message}");
            }
        }

        if (deleteBackupFile)
        {
            DeleteBackupFileIfExists(backupToDelete);
        }
    }

    private void PersistStateLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_state, JsonOptions);
            var tempPath = $"{_stateFilePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _stateFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"ذخیرهٔ وضعیت والپیپر قبلی روی دیسک ناموفق بود: {ex.Message}");
        }
    }

    private string? TryCreateVolatileBackup(string? sourcePath)
    {
        if (!IsWindowsVolatileThemePath(sourcePath) || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(sourcePath);
            var extension = ImageFormatValidator.TryDetectImageExtension(bytes, out var detectedExt)
                ? detectedExt
                : ".img";

            Directory.CreateDirectory(_backupDirectory);
            foreach (var staleBackup in Directory.GetFiles(_backupDirectory, "original_wallpaper.*"))
            {
                DeleteBackupFileIfExists(staleBackup);
            }

            var targetPath = Path.Combine(_backupDirectory, $"original_wallpaper{extension}");
            var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch (Exception ex)
        {
            _logger.Warning($"ایجاد نسخهٔ پشتیبان از فایل موقت ویندوز ناموفق بود: {ex.Message}");
            return null;
        }
    }

    private void DeleteBackupFileIfExists(string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }

        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch
        {
            // Non-fatal cleanup
        }
    }

    private bool HasExternalManualWallpaperChange(DesktopWallpaperSnapshot currentOs, WallpaperBackupState persisted)
    {
        if (IsAppManagedPath(currentOs.WallpaperPath, persisted.LastAppliedByAppPath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(currentOs.WallpaperPath))
        {
            return !PathsEqual(currentOs.WallpaperPath, persisted.OriginalWallpaperPath);
        }

        return currentOs.BackgroundMode == WindowsBackgroundMode.SolidColor &&
               !string.IsNullOrWhiteSpace(persisted.LastAppliedByAppPath);
    }

    private bool IsAppManagedPath(string? candidatePath, string? lastAppliedByAppPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(lastAppliedByAppPath) && PathsEqual(candidatePath, lastAppliedByAppPath))
        {
            return true;
        }

        return IsPathInsideDirectory(candidatePath, _stagingDirectory) ||
               IsPathInsideDirectory(candidatePath, _managedCacheDirectory);
    }

    private static bool IsWindowsVolatileThemePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('/', '\\');
        return normalized.Contains(@"\Microsoft\Windows\Themes\", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFileName(normalized), "TranscodedWallpaper", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideDirectory(string filePath, string directoryPath)
    {
        try
        {
            var fullFile = Path.GetFullPath(filePath);
            var fullDir = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
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
