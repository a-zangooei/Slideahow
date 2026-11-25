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

    public string? GetCurrentDesktopWallpaper()
    {
        var buffer = new char[260];
        if (!SystemParametersInfoGet(SpiGetDeskWallpaper, (uint)buffer.Length, buffer, 0))
        {
            return null;
        }

        var path = new string(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(path) ? null : path;
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
        try
        {
            ApplyFitStyleToRegistry(fitMode);
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
            return new PlatformWallpaperResult(true, 0, "تصویر پس‌زمینهٔ ویندوز با موفقیت تغییر کرد.");
        }

        var errorCode = Marshal.GetLastWin32Error();
        var win32Message = new Win32Exception(errorCode).Message;
        return new PlatformWallpaperResult(
            false,
            errorCode,
            $"تغییر والپیپر ویندوز ناموفق بود (کد Win32: {errorCode} — {win32Message}).");
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

    private static void ApplyFitStyleToRegistry(WallpaperFitMode fitMode)
    {
        using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        if (desktopKey is null)
        {
            return;
        }

        var (style, tile) = fitMode switch
        {
            WallpaperFitMode.Fill => ("10", "0"),
            WallpaperFitMode.Fit => ("6", "0"),
            WallpaperFitMode.Stretch => ("2", "0"),
            WallpaperFitMode.Center => ("0", "0"),
            WallpaperFitMode.Span => ("22", "0"),
            _ => ("10", "0")
        };

        desktopKey.SetValue("WallpaperStyle", style, RegistryValueKind.String);
        desktopKey.SetValue("TileWallpaper", tile, RegistryValueKind.String);
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
