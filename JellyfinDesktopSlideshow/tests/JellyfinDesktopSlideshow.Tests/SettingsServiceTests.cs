using System.IO;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-set-test-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("http://192.168.1.10:8096", true, false)]
    [InlineData("http://localhost:8096", true, false)]
    [InlineData("http://media.example.com:8096", true, true)]
    [InlineData("https://media.example.com", true, false)]
    [InlineData("ftp://192.168.1.10", false, false)]
    [InlineData("", false, false)]
    public void UrlValidationAndPlainHttpWarning_WorkAsExpected(string url, bool expectedValid, bool expectedHttpWarning)
    {
        var service = new SettingsService(new NullLoggingService(), Path.Combine(_tempDir, "settings.json"));
        var valid = service.TryValidateServerUrl(url, out var uri, out _);

        Assert.Equal(expectedValid, valid);
        if (expectedValid && uri is not null)
        {
            Assert.Equal(expectedHttpWarning, service.IsNonLocalPlainHttp(uri));
        }
    }

    [Fact]
    public void Load_ClampsOutOfRangeValuesAndNeverStoresSecrets()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(new NullLoggingService(), path);

        var settings = new AppSettings
        {
            ServerUrl = "https://jellyfin.local",
            Username = "reza",
            SlideshowIntervalSeconds = 1,
            OverlaySize = 300,
            OverlayOpacity = 5.0,
            ShowOverlayIcon = false,
            MinimizeToTrayOnClose = false
        };

        service.Save(settings);

        var rawJson = File.ReadAllText(path);
        Assert.DoesNotContain("Password", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pw\"", rawJson, StringComparison.OrdinalIgnoreCase);

        var reloaded = new SettingsService(new NullLoggingService(), path).Load();
        Assert.Equal("JellyfinDesktopSlideshow:AccessToken", reloaded.TokenCredentialKey);
        Assert.Equal(5, reloaded.SlideshowIntervalSeconds);
        Assert.Equal(96, reloaded.OverlaySize);
        Assert.Equal(1.0, reloaded.OverlayOpacity);
        Assert.False(reloaded.ShowOverlayIcon);
        Assert.False(reloaded.MinimizeToTrayOnClose);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
