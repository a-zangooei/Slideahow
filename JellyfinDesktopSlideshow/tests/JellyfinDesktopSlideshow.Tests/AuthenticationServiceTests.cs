using System.IO;
using System.Net;
using System.Net.Http;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class AuthenticationServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-auth-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoginAsync_StoresTokenInSecureStorage_NotInSettingsJson()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings.json");

        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.0"}""");
            }

            return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"User":{"Id":"u-1","Name":"sara"},"AccessToken":"super-secret-token","ServerId":"srv-1"}""");
        });

        var logger = new NullLoggingService();
        var settingsService = new SettingsService(logger, settingsPath);
        settingsService.Load();

        var secureStorage = new InMemorySecureStorageService();
        var client = new JellyfinClient(new HttpClient(handler), logger);
        var authService = new AuthenticationService(client, settingsService, secureStorage, logger);

        var session = await authService.LoginAsync("http://192.168.1.10:8096", "sara", "my-pass");

        Assert.True(session.IsAuthenticated);
        Assert.Equal("super-secret-token", secureStorage.ReadSecret(settingsService.Current.TokenCredentialKey));

        var savedJson = await File.ReadAllTextAsync(settingsPath);
        Assert.DoesNotContain("super-secret-token", savedJson);
        Assert.DoesNotContain("my-pass", savedJson);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
