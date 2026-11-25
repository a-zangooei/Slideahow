using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed record MonitorResolutionInfo(
    string DeviceName,
    int Width,
    int Height,
    bool IsPrimary);

public sealed record PlatformWallpaperResult(
    bool Succeeded,
    int Win32ErrorCode,
    string Message);

public interface IWallpaperPlatformAdapter
{
    string? GetCurrentDesktopWallpaper();
    string EnsureCompatibleImageFormat(string sourceImagePath, string stagingDirectory);
    PlatformWallpaperResult SetDesktopWallpaper(string imagePath, WallpaperFitMode fitMode);
    IReadOnlyList<MonitorResolutionInfo> GetMonitors();
}
