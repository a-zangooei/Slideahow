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

    [Fact]
    public async Task LoginWithApiKeyAsync_StoresKeyInSecureStorageAndRestoresWithoutUserId()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var seenPaths = new List<string>();

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            seenPaths.Add(path);

            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.3"}""");
            }

            if (path.EndsWith("/System/Info", StringComparison.Ordinal))
            {
                var authParam = req.Headers.Authorization?.Parameter ?? string.Empty;
                if (authParam.Contains("Token=\"valid-api-key-123\"", StringComparison.Ordinal))
                {
                    return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","Version":"10.10.3"}""");
                }

                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            if (path.EndsWith("/Users/Me", StringComparison.Ordinal) || path.EndsWith("/Sessions/Logout", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var logger = new NullLoggingService();
        var settingsService = new SettingsService(logger, settingsPath);
        settingsService.Load();

        var secureStorage = new InMemorySecureStorageService();
        var client = new JellyfinClient(new HttpClient(handler), logger);
        var authService = new AuthenticationService(client, settingsService, secureStorage, logger);

        // Invalid key is rejected cleanly
        await Assert.ThrowsAsync<JellyfinApiException>(() =>
            authService.LoginWithApiKeyAsync("http://192.168.1.10:8096", "bad-key"));
        Assert.Null(secureStorage.ReadSecret(settingsService.Current.TokenCredentialKey));

        // Valid key succeeds, stores secret in secure storage only, and sets ApiKey mode
        var session = await authService.LoginWithApiKeyAsync("http://192.168.1.10:8096", "  valid-api-key-123  ");
        Assert.True(session.IsAuthenticated);
        Assert.True(session.IsApiKeySession);
        Assert.Null(session.UserId);
        Assert.Equal("valid-api-key-123", secureStorage.ReadSecret(settingsService.Current.TokenCredentialKey));
        Assert.Equal("ApiKey", settingsService.Current.AuthMethod);

        var savedJson = await File.ReadAllTextAsync(settingsPath);
        Assert.DoesNotContain("valid-api-key-123", savedJson);

        // Restoring on cold start validates via /System/Info (never /Users/Me) without requiring UserId
        var reloadedSettings = new SettingsService(logger, settingsPath);
        reloadedSettings.Load();
        var restoredAuth = new AuthenticationService(client, reloadedSettings, secureStorage, logger);
        var restoredSession = await restoredAuth.TryRestoreSessionAsync();

        Assert.True(restoredSession.IsAuthenticated);
        Assert.True(restoredSession.IsApiKeySession);
        Assert.DoesNotContain(seenPaths, p => p.EndsWith("/Users/Me", StringComparison.Ordinal));

        // Logout clears local secret without calling /Sessions/Logout
        await restoredAuth.LogoutAsync();
        Assert.False(restoredAuth.CurrentSession.IsAuthenticated);
        Assert.Null(secureStorage.ReadSecret(settingsService.Current.TokenCredentialKey));
        Assert.DoesNotContain(seenPaths, p => p.EndsWith("/Sessions/Logout", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
