using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class WindowsDesktopShortcutAdapter : IDesktopShortcutPlatformAdapter
{
    public string GetDesktopDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public bool ShortcutExists(string shortcutPath)
        => File.Exists(shortcutPath);

    public void CreateOrUpdateShortcut(
        string shortcutPath,
        string targetExePath,
        string arguments,
        string description,
        string iconPath)
    {
        var directory = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var shellLink = (IShellLinkW)new ShellLink();
        try
        {
            shellLink.SetPath(targetExePath);
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                shellLink.SetArguments(arguments);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                shellLink.SetDescription(description);
            }

            var workingDir = Path.GetDirectoryName(targetExePath);
            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                shellLink.SetWorkingDirectory(workingDir);
            }

            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                throw new FileNotFoundException("فایل آیکون استاندارد میان‌بُر پیدا نشد.", iconPath);
            }

            shellLink.SetIconLocation(iconPath, 0);

            // SW_SHOWMINNOACTIVE avoids flashing a console/window frame when invoking --open-current.
            shellLink.SetShowCmd(7);

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }
        finally
        {
            if (Marshal.IsComObject(shellLink))
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }

    public void DeleteShortcut(string shortcutPath)
    {
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    public string EnsureDesktopIconFile(string iconPath)
    {
        if (IsValidIcoFile(iconPath))
        {
            return iconPath;
        }

        var dir = Path.GetDirectoryName(iconPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var icoBytes = BuildMultiSizePngIco([48, 128]);
        var tempPath = $"{iconPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, icoBytes);
            File.Move(tempPath, iconPath, overwrite: true);
            tempPath = null!;
            return iconPath;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore best-effort temp cleanup.
                }
            }
        }
    }

    internal static bool IsValidIcoFile(string iconPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                return false;
            }

            var info = new FileInfo(iconPath);
            if (info.Length < 128)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[6];
            using var stream = File.OpenRead(iconPath);
            if (stream.Read(header) < 6)
            {
                return false;
            }

            // ICONDIR: Reserved == 0, Type == 1 (ICO), Count >= 1
            var reserved = header[0] | (header[1] << 8);
            var type = header[2] | (header[3] << 8);
            var count = header[4] | (header[5] << 8);
            return reserved == 0 && type == 1 && count >= 1 && info.Length >= 6 + (count * 16);
        }
        catch
        {
            return false;
        }
    }

    internal static byte[] BuildMultiSizePngIco(int[] sizes)
    {
        var pngBlobs = sizes.Select(RenderBadgePng).ToArray();
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        // ICONDIR header (6 bytes)
        writer.Write((ushort)0); // Reserved
        writer.Write((ushort)1); // Type: 1 = ICO
        writer.Write((ushort)sizes.Length);

        var nextOffset = 6 + sizes.Length * 16;
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            var png = pngBlobs[i];
            byte dim = size >= 256 ? (byte)0 : (byte)size;

            // ICONDIRENTRY (16 bytes)
            writer.Write(dim); // Width
            writer.Write(dim); // Height
            writer.Write((byte)0); // Color count
            writer.Write((byte)0); // Reserved
            writer.Write((ushort)1); // Color planes
            writer.Write((ushort)32); // Bits per pixel
            writer.Write((uint)png.Length); // Size in bytes
            writer.Write((uint)nextOffset); // Offset in file
            nextOffset += png.Length;
        }

        foreach (var png in pngBlobs)
        {
            writer.Write(png);
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] RenderBadgePng(int size)
    {
        var pixels = new byte[size * size * 4]; // BGRA32
        var center = (size - 1) / 2.0;
        var outerRadius = size * 0.45;
        var innerRadius = outerRadius - Math.Max(1.5, size * 0.045);

        // Play triangle vertices scaled proportionally inside the badge
        var triLeft = size * 0.39;
        var triRight = size * 0.69;
        var triHalfHeight = size * 0.21;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var offset = (y * size + x) * 4;

                if (dist > outerRadius + 0.75)
                {
                    continue;
                }

                // Soft anti-aliased edge coverage
                var alphaFactor = Math.Clamp(outerRadius + 0.5 - dist, 0.0, 1.0);
                var alpha = (byte)Math.Round(alphaFactor * 255);

                byte b, g, r;
                if (dist >= innerRadius)
                {
                    // Subtle slate-white rim matching OverlayWindow
                    r = 226;
                    g = 232;
                    b = 240;
                }
                else if (x >= triLeft && x <= triRight)
                {
                    var progress = (x - triLeft) / (triRight - triLeft);
                    var maxYDelta = (1.0 - progress) * triHalfHeight;
                    if (Math.Abs(y - center) <= maxYDelta)
                    {
                        r = 255;
                        g = 255;
                        b = 255;
                    }
                    else
                    {
                        // Jellyfin cyan accent #00A4DC
                        r = 0x00;
                        g = 0xA4;
                        b = 0xDC;
                    }
                }
                else
                {
                    r = 0x00;
                    g = 0xA4;
                    b = 0xDC;
                }

                pixels[offset + 0] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = alpha;
            }
        }

        var bitmap = BitmapSource.Create(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            size * 4);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}

public sealed class DesktopShortcutService : IDesktopShortcutService
{
    public const string OpenCurrentArgument = "--open-current";
    public const string ShortcutFileName = "باز کردن در Jellyfin.lnk";
    public const string ShortcutDescription = "باز کردن عنوان در حال نمایش روی دسکتاپ در Jellyfin";

    private readonly IDesktopShortcutPlatformAdapter _adapter;
    private readonly ILoggingService _logger;
    private readonly Func<string?> _executablePathProvider;
    private readonly string _iconFilePath;

    public DesktopShortcutService(
        IDesktopShortcutPlatformAdapter adapter,
        ILoggingService logger,
        Func<string?>? executablePathProvider = null,
        string? iconFilePath = null)
    {
        _adapter = adapter;
        _logger = logger;
        _executablePathProvider = executablePathProvider ?? ResolveCurrentExecutablePath;
        _iconFilePath = iconFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JellyfinDesktopSlideshow",
            "icons",
            "jellyfin-desktop.ico");
    }

    public string ShortcutFilePath
        => Path.Combine(_adapter.GetDesktopDirectory(), ShortcutFileName);

    public bool IsShortcutPresent()
    {
        try
        {
            return _adapter.ShortcutExists(ShortcutFilePath);
        }
        catch (Exception ex)
        {
            _logger.Warning($"بررسی وضعیت میان‌بُر دسکتاپ ناموفق بود: {ex.Message}");
            return false;
        }
    }

    public DesktopShortcutUpdateResult CreateOrUpdateShortcut()
    {
        var shortcutPath = string.Empty;
        var alreadyExisted = false;

        try
        {
            shortcutPath = ShortcutFilePath;
            alreadyExisted = _adapter.ShortcutExists(shortcutPath);

            var exePath = _executablePathProvider();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                const string message = "مسیر فایل اجرایی برنامه برای ساخت میان‌بُر دسکتاپ شناسایی نشد.";
                _logger.Warning(message);
                return new DesktopShortcutUpdateResult(false, alreadyExisted, shortcutPath, message);
            }

            var iconPath = _adapter.EnsureDesktopIconFile(_iconFilePath);
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                const string message = "ساخت یا شناسایی فایل آیکون استاندارد میان‌بُر ناموفق بود.";
                _logger.Warning(message);
                return new DesktopShortcutUpdateResult(false, alreadyExisted, shortcutPath, message);
            }

            _adapter.CreateOrUpdateShortcut(
                shortcutPath,
                exePath,
                OpenCurrentArgument,
                ShortcutDescription,
                iconPath);

            var messageText = alreadyExisted
                ? "میان‌بر استاندارد دسکتاپ («باز کردن در Jellyfin») بدون ایجاد نسخهٔ تکراری به‌روزرسانی شد."
                : "میان‌بر استاندارد «باز کردن در Jellyfin» روی دسکتاپ ساخته شد.";

            return new DesktopShortcutUpdateResult(true, alreadyExisted, shortcutPath, messageText);
        }
        catch (Exception ex)
        {
            _logger.Error("به‌روزرسانی میان‌بُر استاندارد دسکتاپ ناموفق بود.", ex);
            return new DesktopShortcutUpdateResult(
                false,
                alreadyExisted,
                shortcutPath,
                $"ساخت یا به‌روزرسانی میان‌بُر دسکتاپ ناموفق بود: {ex.Message}");
        }
    }

    public bool SetShortcutEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                _adapter.DeleteShortcut(ShortcutFilePath);
                return true;
            }

            return CreateOrUpdateShortcut().Succeeded;
        }
        catch (Exception ex)
        {
            _logger.Error("تغییر وضعیت میان‌بُر استاندارد دسکتاپ ناموفق بود.", ex);
            return false;
        }
    }

    private static string? ResolveCurrentExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
