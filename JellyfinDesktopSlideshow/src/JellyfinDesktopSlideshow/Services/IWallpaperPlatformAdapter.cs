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

public enum WindowsBackgroundMode
{
    Unknown = -1,
    Picture = 0,
    SolidColor = 1,
    Slideshow = 2,
    Spotlight = 3
}

public sealed record DesktopWallpaperSnapshot(
    string? WallpaperPath,
    string? WallpaperStyle,
    string? TileWallpaper,
    WindowsBackgroundMode BackgroundMode = WindowsBackgroundMode.Unknown);

public interface IWallpaperPlatformAdapter
{
    DesktopWallpaperSnapshot GetCurrentDesktopWallpaperState();
    string EnsureCompatibleImageFormat(string sourceImagePath, string stagingDirectory);
    PlatformWallpaperResult SetDesktopWallpaper(string imagePath, WallpaperFitMode fitMode);
    PlatformWallpaperResult RestoreDesktopWallpaper(string imagePath, string? wallpaperStyle, string? tileWallpaper);
    IReadOnlyList<MonitorResolutionInfo> GetMonitors();
}
