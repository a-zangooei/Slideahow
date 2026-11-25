using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;

namespace JellyfinDesktopSlideshow.Services;

public sealed class WallpaperService : IWallpaperService
{
    private readonly IWallpaperPlatformAdapter _platformAdapter;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly string _stagingDirectory;
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    private string? _originalWallpaperPath;
    private bool _capturedOriginalWallpaper;

    public string? LastAppliedWallpaperPath { get; private set; }

    public WallpaperService(
        IWallpaperPlatformAdapter platformAdapter,
        ISettingsService settingsService,
        ILoggingService logger,
        string? stagingDirectory = null)
    {
        _platformAdapter = platformAdapter;
        _settingsService = settingsService;
        _logger = logger;
        _stagingDirectory = stagingDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JellyfinDesktopSlideshow",
            "cache",
            "wallpaper");
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
            if (!_capturedOriginalWallpaper)
            {
                _originalWallpaperPath = _platformAdapter.GetCurrentDesktopWallpaper();
                _capturedOriginalWallpaper = true;
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
                LastAppliedWallpaperPath = compatiblePath;
                return new WallpaperApplyResult(true, result.Message, compatiblePath, 0);
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
        if (!_capturedOriginalWallpaper ||
            string.IsNullOrWhiteSpace(_originalWallpaperPath) ||
            !File.Exists(_originalWallpaperPath))
        {
            return new WallpaperApplyResult(false, "والپیپر اولیه برای بازگردانی در دسترس نیست.");
        }

        return await ApplyWallpaperAsync(_originalWallpaperPath, cancellationToken);
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
}
