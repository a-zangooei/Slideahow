using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed record WallpaperApplyResult(
    bool Succeeded,
    string Message,
    string? AppliedFilePath = null,
    int Win32ErrorCode = 0);

public interface IWallpaperService
{
    string? LastAppliedWallpaperPath { get; }
    bool HasCapturedOriginalWallpaper { get; }
    Task<WallpaperApplyResult> ApplyWallpaperAsync(string imagePath, CancellationToken cancellationToken = default);
    Task<WallpaperApplyResult> ReapplyCurrentWallpaperAsync(CancellationToken cancellationToken = default);
    Task<WallpaperApplyResult> RestoreOriginalWallpaperAsync(CancellationToken cancellationToken = default);
    string GetMonitorsSummary();
}
