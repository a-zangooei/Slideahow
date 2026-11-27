using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class TrayService : ITrayService
{
    private const int TrayCallbackMessage = 0x0400 + 0x101; // WM_USER + 257
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private readonly ILoggingService _logger;
    private readonly Localization.ILocalizationService _localization;
    private HwndSource? _hwndSource;
    private IntPtr _iconHandle;
    private bool _ownsIconHandle;
    private uint _taskbarCreatedMessage;
    private bool _iconAdded;
    private bool _disposed;

    private string _tooltipText = "Jellyfin Desktop Slideshow";
    private bool _isPaused;
    private bool _canTogglePause;
    private bool _canOpenCurrentItem;

    private ContextMenu? _contextMenu;
    private MenuItem? _showItem;
    private MenuItem? _settingsItem;
    private MenuItem? _pauseResumeItem;
    private MenuItem? _nextItem;
    private MenuItem? _openCurrentItemMenu;
    private MenuItem? _desktopShortcutItem;
    private MenuItem? _exitItem;

    public event EventHandler? ShowMainWindowRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? TogglePauseRequested;
    public event EventHandler? NextSlideRequested;
    public event EventHandler? OpenCurrentItemRequested;
    public event EventHandler? CreateDesktopShortcutRequested;
    public event EventHandler? ExitApplicationRequested;

    public TrayService(ILoggingService logger, Localization.ILocalizationService? localization = null)
    {
        _logger = logger;
        _localization = localization ?? Localization.AppLocalization.Current;
        _tooltipText = _localization.GetString("Tray.ToolTip.Default");
    }

    public void Initialize()
    {
        if (_disposed || _hwndSource is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("JellyfinDesktopSlideshowTrayHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        CreateIconHandle();
        BuildContextMenu();
        _localization.LanguageChanged += OnLanguageChanged;
        AddOrUpdateTrayIcon();
    }

    public void UpdateState(string tooltipText, bool isPaused, bool canTogglePause, bool canOpenCurrentItem)
    {
        _tooltipText = TruncateTooltip(tooltipText);
        _isPaused = isPaused;
        _canTogglePause = canTogglePause;
        _canOpenCurrentItem = canOpenCurrentItem;

        if (_pauseResumeItem is not null)
        {
            _pauseResumeItem.Header = _localization.GetString(_isPaused ? "Tray.Menu.Resume" : "Tray.Menu.Pause");
            _pauseResumeItem.IsEnabled = _canTogglePause;
        }

        if (_openCurrentItemMenu is not null)
        {
            _openCurrentItemMenu.IsEnabled = _canOpenCurrentItem;
        }

        if (_iconAdded)
        {
            AddOrUpdateTrayIcon(modifyOnly: true);
        }
    }

    public void UpdateLocalization()
    {
        if (_contextMenu is null)
        {
            return;
        }

        _contextMenu.FlowDirection = _localization.CurrentFlowDirection;
        if (_showItem is not null) _showItem.Header = _localization.GetString("Tray.Menu.ShowMain");
        if (_settingsItem is not null) _settingsItem.Header = _localization.GetString("Tray.Menu.Settings");
        if (_pauseResumeItem is not null)
        {
            _pauseResumeItem.Header = _localization.GetString(_isPaused ? "Tray.Menu.Resume" : "Tray.Menu.Pause");
        }
        if (_nextItem is not null) _nextItem.Header = _localization.GetString("Tray.Menu.Next");
        if (_openCurrentItemMenu is not null) _openCurrentItemMenu.Header = _localization.GetString("Tray.Menu.OpenCurrent");
        if (_desktopShortcutItem is not null) _desktopShortcutItem.Header = _localization.GetString("Tray.Menu.CreateShortcut");
        if (_exitItem is not null) _exitItem.Header = _localization.GetString("Tray.Menu.Exit");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        RemoveTrayIcon();

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }

        if (_ownsIconHandle && _iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLocalization();
    }

    private void BuildContextMenu()
    {
        _showItem = new MenuItem();
        _showItem.Click += (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);

        _settingsItem = new MenuItem();
        _settingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        _pauseResumeItem = new MenuItem
        {
            IsEnabled = _canTogglePause
        };
        _pauseResumeItem.Click += (_, _) => TogglePauseRequested?.Invoke(this, EventArgs.Empty);

        _nextItem = new MenuItem();
        _nextItem.Click += (_, _) => NextSlideRequested?.Invoke(this, EventArgs.Empty);

        _openCurrentItemMenu = new MenuItem
        {
            IsEnabled = _canOpenCurrentItem
        };
        _openCurrentItemMenu.Click += (_, _) => OpenCurrentItemRequested?.Invoke(this, EventArgs.Empty);

        _desktopShortcutItem = new MenuItem();
        _desktopShortcutItem.Click += (_, _) => CreateDesktopShortcutRequested?.Invoke(this, EventArgs.Empty);

        _exitItem = new MenuItem();
        _exitItem.Click += (_, _) => ExitApplicationRequested?.Invoke(this, EventArgs.Empty);

        _contextMenu = new ContextMenu();
        _contextMenu.Items.Add(_showItem);
        _contextMenu.Items.Add(_settingsItem);
        _contextMenu.Items.Add(new Separator());
        _contextMenu.Items.Add(_pauseResumeItem);
        _contextMenu.Items.Add(_nextItem);
        _contextMenu.Items.Add(_openCurrentItemMenu);
        _contextMenu.Items.Add(_desktopShortcutItem);
        _contextMenu.Items.Add(new Separator());
        _contextMenu.Items.Add(_exitItem);

        UpdateLocalization();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            _iconAdded = false;
            AddOrUpdateTrayIcon();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == TrayCallbackMessage)
        {
            var eventCode = (int)(lParam.ToInt64() & 0xFFFF);
            if (eventCode is WmLButtonUp or WmLButtonDblClk)
            {
                ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            else if (eventCode is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_contextMenu is null || _hwndSource is null)
        {
            return;
        }

        SetForegroundWindow(_hwndSource.Handle);
        _contextMenu.Placement = PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    private void AddOrUpdateTrayIcon(bool modifyOnly = false)
    {
        if (_hwndSource is null || _hwndSource.Handle == IntPtr.Zero)
        {
            return;
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwndSource.Handle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _iconHandle,
            szTip = _tooltipText
        };

        var op = modifyOnly && _iconAdded ? NimModify : NimAdd;
        if (Shell_NotifyIconW(op, ref data))
        {
            _iconAdded = true;
        }
        else if (!modifyOnly)
        {
            _logger.Warning("افزودن آیکون در System Tray با خطا مواجه شد.");
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded || _hwndSource is null || _hwndSource.Handle == IntPtr.Zero)
        {
            return;
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwndSource.Handle,
            uID = 1
        };

        Shell_NotifyIconW(NimDelete, ref data);
        _iconAdded = false;
    }

    private void CreateIconHandle()
    {
        _iconHandle = TryCreateJellyfinBadgeIcon();
        if (_iconHandle != IntPtr.Zero)
        {
            _ownsIconHandle = true;
            return;
        }

        // Fallback to standard application icon if DIB creation fails.
        _iconHandle = LoadIconW(IntPtr.Zero, new IntPtr(32512));
        _ownsIconHandle = false;
    }

    private static IntPtr TryCreateJellyfinBadgeIcon()
    {
        const int size = 16;
        var bmi = new BITMAPINFO
        {
            biSize = Marshal.SizeOf<BITMAPINFO>(),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        var colorBitmap = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out var bitsPtr, IntPtr.Zero, 0);
        if (colorBitmap == IntPtr.Zero || bitsPtr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var maskBitmap = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        if (maskBitmap == IntPtr.Zero)
        {
            DeleteObject(colorBitmap);
            return IntPtr.Zero;
        }

        try
        {
            var pixels = new int[size * size];
            const double center = 7.5;
            const double radiusSq = 7.2 * 7.2;
            const int bgColor = unchecked((int)0xFF00A4DC); // Jellyfin accent cyan (#00A4DC)
            const int fgColor = unchecked((int)0xFFFFFFFF);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    if (dx * dx + dy * dy <= radiusSq)
                    {
                        var inPlayTriangle =
                            x >= 6 && x <= 11 &&
                            Math.Abs(y - 7.5) <= (11 - x) * 0.72;
                        pixels[y * size + x] = inPlayTriangle ? fgColor : bgColor;
                    }
                    else
                    {
                        pixels[y * size + x] = 0;
                    }
                }
            }

            Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);

            var iconInfo = new ICONINFO
            {
                fIcon = true,
                hbmMask = maskBitmap,
                hbmColor = colorBitmap
            };

            return CreateIconIndirect(ref iconInfo);
        }
        finally
        {
            DeleteObject(colorBitmap);
            DeleteObject(maskBitmap);
        }
    }

    private static string TruncateTooltip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Jellyfin Desktop Slideshow";
        }

        return value.Length <= 127 ? value : string.Concat(value.AsSpan(0, 124), "...");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
