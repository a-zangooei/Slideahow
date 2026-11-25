using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class DesktopShortcutServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-desk-ico-{Guid.NewGuid():N}");

    [Fact]
    public void SetShortcutEnabled_True_CreatesShortcutWithOpenCurrentArgumentAndIcon()
    {
        Directory.CreateDirectory(_tempDir);
        var desktopDir = Path.Combine(_tempDir, "Desktop");
        var iconPath = Path.Combine(_tempDir, "icons", "jellyfin.ico");
        var adapter = new FakeDesktopShortcutAdapter(desktopDir);

        var service = new DesktopShortcutService(
            adapter,
            new NullLoggingService(),
            () => @"C:\Program Files\JellyfinDesktopSlideshow\JellyfinDesktopSlideshow.exe",
            iconPath);

        var ok = service.SetShortcutEnabled(true);

        Assert.True(ok);
        Assert.True(service.IsShortcutPresent());
        Assert.Equal(Path.Combine(desktopDir, DesktopShortcutService.ShortcutFileName), adapter.LastShortcutPath);
        Assert.Equal(@"C:\Program Files\JellyfinDesktopSlideshow\JellyfinDesktopSlideshow.exe", adapter.LastTargetExePath);
        Assert.Equal(DesktopShortcutService.OpenCurrentArgument, adapter.LastArguments);
        Assert.Equal(iconPath, adapter.LastIconPath);
    }

    [Fact]
    public void SetShortcutEnabled_False_DeletesExistingShortcut()
    {
        Directory.CreateDirectory(_tempDir);
        var desktopDir = Path.Combine(_tempDir, "Desktop");
        var adapter = new FakeDesktopShortcutAdapter(desktopDir);

        var service = new DesktopShortcutService(
            adapter,
            new NullLoggingService(),
            () => @"C:\Apps\JellyfinDesktopSlideshow.exe",
            Path.Combine(_tempDir, "icons", "jellyfin.ico"));

        Assert.True(service.SetShortcutEnabled(true));
        Assert.True(service.IsShortcutPresent());

        Assert.True(service.SetShortcutEnabled(false));
        Assert.False(service.IsShortcutPresent());
    }

    [Fact]
    public void SetShortcutEnabled_True_WhenExecutableMissing_ReturnsFalseWithoutCreatingShortcut()
    {
        Directory.CreateDirectory(_tempDir);
        var adapter = new FakeDesktopShortcutAdapter(Path.Combine(_tempDir, "Desktop"));
        var service = new DesktopShortcutService(adapter, new NullLoggingService(), () => null);

        var ok = service.SetShortcutEnabled(true);

        Assert.False(ok);
        Assert.False(service.IsShortcutPresent());
        Assert.Equal(0, adapter.CreateCalls);
    }

    [Fact]
    public void BuildMultiSizePngIco_GeneratesValidMultiEntryIcoHeaderWithPngPayloads()
    {
        var icoBytes = WindowsDesktopShortcutAdapter.BuildMultiSizePngIco([48, 128]);

        Assert.True(icoBytes.Length > 200);
        // Reserved = 0, Type = 1 (ICO), Count = 2
        Assert.Equal(0, icoBytes[0]);
        Assert.Equal(0, icoBytes[1]);
        Assert.Equal(1, icoBytes[2]);
        Assert.Equal(0, icoBytes[3]);
        Assert.Equal(2, icoBytes[4]);
        Assert.Equal(0, icoBytes[5]);

        // Entry 1 dimensions = 48x48, Entry 2 dimensions = 128x128
        Assert.Equal(48, icoBytes[6]);
        Assert.Equal(48, icoBytes[7]);
        Assert.Equal(128, icoBytes[22]);
        Assert.Equal(128, icoBytes[23]);

        // First PNG starts right after 6 + 2*16 = 38 bytes
        Assert.Equal(0x89, icoBytes[38]);
        Assert.Equal((byte)'P', icoBytes[39]);
        Assert.Equal((byte)'N', icoBytes[40]);
        Assert.Equal((byte)'G', icoBytes[41]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class FakeDesktopShortcutAdapter : IDesktopShortcutPlatformAdapter
    {
        private readonly string _desktopDir;
        private readonly HashSet<string> _shortcuts = new(StringComparer.OrdinalIgnoreCase);

        public FakeDesktopShortcutAdapter(string desktopDir)
        {
            _desktopDir = desktopDir;
        }

        public int CreateCalls { get; private set; }
        public string? LastShortcutPath { get; private set; }
        public string? LastTargetExePath { get; private set; }
        public string? LastArguments { get; private set; }
        public string? LastIconPath { get; private set; }

        public string GetDesktopDirectory() => _desktopDir;

        public bool ShortcutExists(string shortcutPath) => _shortcuts.Contains(shortcutPath);

        public void CreateOrUpdateShortcut(
            string shortcutPath,
            string targetExePath,
            string arguments,
            string description,
            string iconPath)
        {
            CreateCalls++;
            LastShortcutPath = shortcutPath;
            LastTargetExePath = targetExePath;
            LastArguments = arguments;
            LastIconPath = iconPath;
            _shortcuts.Add(shortcutPath);
        }

        public void DeleteShortcut(string shortcutPath) => _shortcuts.Remove(shortcutPath);

        public string EnsureDesktopIconFile(string iconPath) => iconPath;
    }
}
