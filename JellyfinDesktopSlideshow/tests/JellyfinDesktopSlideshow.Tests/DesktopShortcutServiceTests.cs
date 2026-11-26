using System.Buffers.Binary;
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
        ReadOnlySpan<byte> span = icoBytes;

        Assert.True(icoBytes.Length > 200);
        // Reserved = 0, Type = 1 (ICO), Count = 2
        Assert.Equal(0, (int)BinaryPrimitives.ReadUInt16LittleEndian(span[0..2]));
        Assert.Equal(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(span[2..4]));
        Assert.Equal(2, (int)BinaryPrimitives.ReadUInt16LittleEndian(span[4..6]));

        // Entry 1 (48x48)
        Assert.Equal(48, icoBytes[6]);
        Assert.Equal(48, icoBytes[7]);
        Assert.Equal(32, (int)BinaryPrimitives.ReadUInt16LittleEndian(span[12..14]));
        var entry1Size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[14..18]));
        var entry1Offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[18..22]));
        Assert.True(entry1Size > 100);
        Assert.Equal(6 + (2 * 16), entry1Offset);

        // Entry 2 (128x128)
        Assert.Equal(128, icoBytes[22]);
        Assert.Equal(128, icoBytes[23]);
        Assert.Equal(32, (int)BinaryPrimitives.ReadUInt16LittleEndian(span[28..30]));
        var entry2Size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[30..34]));
        var entry2Offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[34..38]));
        Assert.True(entry2Size > 100);
        Assert.Equal(entry1Offset + entry1Size, entry2Offset);
        Assert.Equal(icoBytes.Length, entry2Offset + entry2Size);

        // Both payloads must begin with the standard 8-byte PNG signature
        byte[] expectedPngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(expectedPngSignature, icoBytes.AsSpan(entry1Offset, 8).ToArray());
        Assert.Equal(expectedPngSignature, icoBytes.AsSpan(entry2Offset, 8).ToArray());
    }

    [Fact]
    public void CreateOrUpdateShortcut_WhenCalledRepeatedly_UpdatesInPlaceWithoutDuplicates()
    {
        Directory.CreateDirectory(_tempDir);
        var desktopDir = Path.Combine(_tempDir, "Desktop");
        var iconPath = Path.Combine(_tempDir, "icons", "jellyfin.ico");
        var adapter = new FakeDesktopShortcutAdapter(desktopDir);
        var exeVersion = @"C:\App\v1\JellyfinDesktopSlideshow.exe";

        var service = new DesktopShortcutService(
            adapter,
            new NullLoggingService(),
            () => exeVersion,
            iconPath);

        var first = service.CreateOrUpdateShortcut();
        Assert.True(first.Succeeded);
        Assert.False(first.AlreadyExisted);
        Assert.Equal(1, adapter.ShortcutCount);

        exeVersion = @"C:\App\v2\JellyfinDesktopSlideshow.exe";
        var second = service.CreateOrUpdateShortcut();

        Assert.True(second.Succeeded);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(1, adapter.ShortcutCount);
        Assert.Equal(first.ShortcutPath, second.ShortcutPath);
        Assert.Equal(@"C:\App\v2\JellyfinDesktopSlideshow.exe", adapter.LastTargetExePath);
        Assert.Equal(DesktopShortcutService.OpenCurrentArgument, adapter.LastArguments);
        Assert.Equal(DesktopShortcutService.ShortcutDescription, adapter.LastDescription);
        Assert.Equal(iconPath, adapter.LastIconPath);
    }

    [Fact]
    public void CreateOrUpdateShortcut_WhenIconMissingOrGenerationFails_ReturnsClearError()
    {
        Directory.CreateDirectory(_tempDir);
        var desktopDir = Path.Combine(_tempDir, "Desktop");
        var adapter = new FakeDesktopShortcutAdapter(desktopDir)
        {
            IconOverride = string.Empty
        };

        var service = new DesktopShortcutService(
            adapter,
            new NullLoggingService(),
            () => @"C:\Apps\JellyfinDesktopSlideshow.exe",
            Path.Combine(_tempDir, "icons", "missing.ico"));

        var result = service.CreateOrUpdateShortcut();

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(0, adapter.CreateCalls);
        Assert.False(service.IsShortcutPresent());

        adapter.ThrowOnEnsureIcon = true;
        var exceptionResult = service.CreateOrUpdateShortcut();
        Assert.False(exceptionResult.Succeeded);
        Assert.Contains("ناموفق", exceptionResult.Message);
    }

    [Fact]
    public void EnsureDesktopIconFile_RegeneratesCorruptIconAndLeavesNoTempFiles()
    {
        Directory.CreateDirectory(_tempDir);
        var iconPath = Path.Combine(_tempDir, "icons", "jellyfin-desktop.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);

        // Write corrupt non-ICO payload larger than 128 bytes
        File.WriteAllBytes(iconPath, Enumerable.Repeat((byte)0x41, 256).ToArray());
        Assert.False(WindowsDesktopShortcutAdapter.IsValidIcoFile(iconPath));

        var adapter = new WindowsDesktopShortcutAdapter();
        var returnedPath = adapter.EnsureDesktopIconFile(iconPath);

        Assert.Equal(iconPath, returnedPath);
        Assert.True(WindowsDesktopShortcutAdapter.IsValidIcoFile(iconPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(iconPath)!, "*.tmp"));
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
        public int ShortcutCount => _shortcuts.Count;
        public string? LastShortcutPath { get; private set; }
        public string? LastTargetExePath { get; private set; }
        public string? LastArguments { get; private set; }
        public string? LastDescription { get; private set; }
        public string? LastIconPath { get; private set; }
        public string? IconOverride { get; set; }
        public bool ThrowOnEnsureIcon { get; set; }

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
            LastDescription = description;
            LastIconPath = iconPath;
            _shortcuts.Add(shortcutPath);
        }

        public void DeleteShortcut(string shortcutPath) => _shortcuts.Remove(shortcutPath);

        public string EnsureDesktopIconFile(string iconPath)
        {
            if (ThrowOnEnsureIcon)
            {
                throw new IOException("Disk write denied");
            }

            return IconOverride ?? iconPath;
        }
    }
}
