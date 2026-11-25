using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Microsoft.Win32;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class WindowsWallpaperPlatformAdapter : IWallpaperPlatformAdapter
{
    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpiGetDeskWallpaper = 0x0073;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;
    private const uint MonitorInfoFPrimary = 0x00000001;

    public DesktopWallpaperSnapshot GetCurrentDesktopWallpaperState()
    {
        string? wallpaperPath = null;
        var buffer = new char[1024];
        if (SystemParametersInfoGet(SpiGetDeskWallpaper, (uint)buffer.Length, buffer, 0))
        {
            var rawPath = new string(buffer).TrimEnd('\0');
            if (!string.IsNullOrWhiteSpace(rawPath))
            {
                wallpaperPath = rawPath;
            }
        }

        string? wallpaperStyle = null;
        string? tileWallpaper = null;
        try
        {
            using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
            wallpaperStyle = desktopKey?.GetValue("WallpaperStyle")?.ToString();
            tileWallpaper = desktopKey?.GetValue("TileWallpaper")?.ToString();
        }
        catch
        {
            // Non-fatal: partial metadata capture should not block wallpaper state handling.
        }

        var backgroundMode = ReadWindowsBackgroundMode();
        return new DesktopWallpaperSnapshot(wallpaperPath, wallpaperStyle, tileWallpaper, backgroundMode);
    }

    public string EnsureCompatibleImageFormat(string sourceImagePath, string stagingDirectory)
    {
        var ext = Path.GetExtension(sourceImagePath);
        if (!string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return sourceImagePath;
        }

        Directory.CreateDirectory(stagingDirectory);
        var convertedPath = Path.Combine(stagingDirectory, "desktop_wallpaper.png");

        using var inputStream = File.OpenRead(sourceImagePath);
        var decoder = BitmapDecoder.Create(
            inputStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));

        var tempPath = $"{convertedPath}.tmp";
        using (var outputStream = File.Create(tempPath))
        {
            encoder.Save(outputStream);
        }

        File.Move(tempPath, convertedPath, overwrite: true);
        return convertedPath;
    }

    public PlatformWallpaperResult SetDesktopWallpaper(string imagePath, WallpaperFitMode fitMode)
    {
        var (style, tile) = MapFitModeToRegistryValues(fitMode);
        return ApplyWallpaperInternal(
            imagePath,
            style,
            tile,
            "تصویر پس‌زمینهٔ ویندوز با موفقیت تغییر کرد.");
    }

    public PlatformWallpaperResult RestoreDesktopWallpaper(string imagePath, string? wallpaperStyle, string? tileWallpaper)
    {
        return ApplyWallpaperInternal(
            imagePath,
            wallpaperStyle,
            tileWallpaper,
            "والپیپر و تنظیمات چینش قبلی ویندوز با موفقیت بازگردانی شد.");
    }

    public IReadOnlyList<MonitorResolutionInfo> GetMonitors()
    {
        var monitors = new List<MonitorResolutionInfo>();

        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
            {
                var info = new MONITORINFOEXW
                {
                    cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>()
                };

                if (GetMonitorInfoW(hMonitor, ref info))
                {
                    var width = Math.Abs(info.rcMonitor.Right - info.rcMonitor.Left);
                    var height = Math.Abs(info.rcMonitor.Bottom - info.rcMonitor.Top);
                    var isPrimary = (info.dwFlags & MonitorInfoFPrimary) != 0;
                    monitors.Add(new MonitorResolutionInfo(info.szDevice, width, height, isPrimary));
                }

                return true;
            },
            IntPtr.Zero);

        return monitors;
    }

    private static PlatformWallpaperResult ApplyWallpaperInternal(
        string imagePath,
        string? wallpaperStyle,
        string? tileWallpaper,
        string successMessage)
    {
        try
        {
            ApplyStyleValuesToRegistry(wallpaperStyle, tileWallpaper);
        }
        catch
        {
            // Non-fatal: still attempt SystemParametersInfoW.
        }

        var ok = SystemParametersInfoSet(
            SpiSetDeskWallpaper,
            0,
            imagePath,
            SpifUpdateIniFile | SpifSendChange);

        if (ok)
        {
            return new PlatformWallpaperResult(true, 0, successMessage);
        }

        var errorCode = Marshal.GetLastWin32Error();
        var win32Message = new Win32Exception(errorCode).Message;
        return new PlatformWallpaperResult(
            false,
            errorCode,
            $"تغییر والپیپر ویندوز ناموفق بود (کد Win32: {errorCode} — {win32Message}).");
    }

    private static (string Style, string Tile) MapFitModeToRegistryValues(WallpaperFitMode fitMode)
        => fitMode switch
        {
            WallpaperFitMode.Fill => ("10", "0"),
            WallpaperFitMode.Fit => ("6", "0"),
            WallpaperFitMode.Stretch => ("2", "0"),
            WallpaperFitMode.Center => ("0", "0"),
            WallpaperFitMode.Span => ("22", "0"),
            _ => ("10", "0")
        };

    private static void ApplyStyleValuesToRegistry(string? wallpaperStyle, string? tileWallpaper)
    {
        if (wallpaperStyle is null && tileWallpaper is null)
        {
            return;
        }

        using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        if (desktopKey is null)
        {
            return;
        }

        if (wallpaperStyle is not null)
        {
            desktopKey.SetValue("WallpaperStyle", wallpaperStyle, RegistryValueKind.String);
        }

        if (tileWallpaper is not null)
        {
            desktopKey.SetValue("TileWallpaper", tileWallpaper, RegistryValueKind.String);
        }
    }

    private static WindowsBackgroundMode ReadWindowsBackgroundMode()
    {
        try
        {
            using var wallpapersKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers",
                writable: false);
            var rawValue = wallpapersKey?.GetValue("BackgroundType");
            if (rawValue is null)
            {
                return WindowsBackgroundMode.Unknown;
            }

            var numericValue = Convert.ToInt32(rawValue);
            return numericValue switch
            {
                0 => WindowsBackgroundMode.Picture,
                1 => WindowsBackgroundMode.SolidColor,
                2 => WindowsBackgroundMode.Slideshow,
                3 => WindowsBackgroundMode.Spotlight,
                _ => WindowsBackgroundMode.Unknown
            };
        }
        catch
        {
            return WindowsBackgroundMode.Unknown;
        }
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoSet(
        uint uiAction,
        uint uiParam,
        string pvParam,
        uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoGet(
        uint uiAction,
        uint uiParam,
        [Out] char[] pvParam,
        uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
