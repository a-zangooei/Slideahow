using System.IO;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class ItemLauncherTests
{
    private static readonly MediaItem SampleMovie = new()
    {
        Id = "movie123",
        Name = "Blade Runner 2049",
        Type = "Movie",
        ProductionYear = 2017
    };

    [Fact]
    public async Task OpenItemAsync_DefaultBrowser_BuildsOfficialWebDetailsUrlWithServerId()
    {
        using var scope = new TempLauncherScope();
        var launcher = scope.CreateLauncher();

        var result = await launcher.OpenItemAsync(SampleMovie);

        Assert.True(result.Succeeded);
        Assert.Single(scope.ExternalLauncher.OpenedUrls);
        Assert.Equal(
            "http://192.168.1.10:8096/web/index.html#!/details?id=movie123&serverId=srv001",
            scope.ExternalLauncher.OpenedUrls[0].AbsoluteUri);
    }

    [Fact]
    public async Task OpenItemAsync_DesktopClientNotInstalled_ReturnsFallbackBrowserPromptData()
    {
        using var scope = new TempLauncherScope();
        scope.Settings.PlaybackTarget.TargetType = PlaybackTargetType.JellyfinDesktopClient;
        scope.ExternalLauncher.DesktopInstalled = false;
        var launcher = scope.CreateLauncher();

        var result = await launcher.OpenItemAsync(SampleMovie);

        Assert.False(result.Succeeded);
        Assert.True(result.CanFallbackToBrowser);
        Assert.Equal(
            "http://192.168.1.10:8096/web/index.html#!/details?id=movie123&serverId=srv001",
            result.FallbackBrowserUrl);
        Assert.Empty(scope.ExternalLauncher.OpenedUrls);
    }

    [Fact]
    public async Task OpenItemAsync_DesktopClientWithActiveRemoteSession_SendsViewingCommand()
    {
        using var scope = new TempLauncherScope();
        scope.Settings.PlaybackTarget.TargetType = PlaybackTargetType.JellyfinDesktopClient;
        scope.ExternalLauncher.DesktopInstalled = true;
        scope.Client.Sessions =
        [
            new JellyfinSessionInfo
            {
                Id = "sess-jmp-1",
                Client = "Jellyfin Media Player",
                DeviceId = "other-device-id",
                DeviceName = "DESKTOP-PC",
                SupportsRemoteControl = true
            }
        ];

        var launcher = scope.CreateLauncher();

        var result = await launcher.OpenItemAsync(SampleMovie);

        Assert.True(result.Succeeded);
        Assert.Single(scope.Client.ViewingCalls);
        var call = scope.Client.ViewingCalls[0];
        Assert.Equal("sess-jmp-1", call.SessionId);
        Assert.Equal("movie123", call.ItemId);
        Assert.Equal("Movie", call.ItemType);
    }

    [Fact]
    public async Task OpenItemAsync_DesktopClientWithoutRemoteSession_OffersBrowserFallback()
    {
        using var scope = new TempLauncherScope();
        scope.Settings.PlaybackTarget.TargetType = PlaybackTargetType.JellyfinDesktopClient;
        scope.ExternalLauncher.DesktopInstalled = true;
        scope.Client.Sessions =
        [
            new JellyfinSessionInfo
            {
                Id = "sess-readonly",
                Client = "Jellyfin Media Player",
                DeviceId = "other-device-id",
                SupportsRemoteControl = false
            }
        ];

        var launcher = scope.CreateLauncher();

        var result = await launcher.OpenItemAsync(SampleMovie);

        Assert.False(result.Succeeded);
        Assert.True(result.CanFallbackToBrowser);
        Assert.NotNull(result.FallbackBrowserUrl);
        Assert.Empty(scope.Client.ViewingCalls);
    }

    [Theory]
    [InlineData("cmd.exe /c calc")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com/?q=\"quoted\"")]
    [InlineData("https://example.com/with space/{itemId}")]
    public void TryValidateCustomUrlTemplate_RejectsUnsafeOrNonHttpTemplates(string unsafeTemplate)
    {
        using var scope = new TempLauncherScope();
        var launcher = scope.CreateLauncher();

        var isValid = launcher.TryValidateCustomUrlTemplate(unsafeTemplate, out var errorMessage);

        Assert.False(isValid);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    [Fact]
    public async Task OpenItemAsync_ValidCustomUrlTemplate_ExpandsPlaceholdersAndOpensHttpUrl()
    {
        using var scope = new TempLauncherScope();
        scope.Settings.PlaybackTarget.TargetType = PlaybackTargetType.CustomUrlTemplate;
        scope.Settings.PlaybackTarget.CustomUrlTemplate = "{baseUrl}/web/index.html#!/details?id={itemId}&serverId={serverId}";
        var launcher = scope.CreateLauncher();

        var result = await launcher.OpenItemAsync(SampleMovie);

        Assert.True(result.Succeeded);
        Assert.Single(scope.ExternalLauncher.OpenedUrls);
        Assert.Equal(
            "http://192.168.1.10:8096/web/index.html#!/details?id=movie123&serverId=srv001",
            scope.ExternalLauncher.OpenedUrls[0].AbsoluteUri);
    }

    private sealed class TempLauncherScope : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "JellyfinSlideshowLauncherTests", Guid.NewGuid().ToString("N"));

        public AppSettings Settings { get; }
        public SettingsService SettingsService { get; }
        public FakeJellyfinClient Client { get; } = new();
        public FakeAuthenticationService AuthService { get; } = new();
        public FakeExternalLauncher ExternalLauncher { get; } = new();

        public TempLauncherScope()
        {
            Directory.CreateDirectory(_tempDir);
            var settingsPath = Path.Combine(_tempDir, "settings.json");
            SettingsService = new SettingsService(new NullLoggingService(), settingsPath);
            Settings = SettingsService.Current;
            Settings.ServerUrl = "http://192.168.1.10:8096";
            Settings.ServerId = "srv001";
            Settings.DeviceId = "this-app-device";
        }

        public ItemLauncher CreateLauncher()
            => new(Client, AuthService, SettingsService, ExternalLauncher, new NullLoggingService());

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
    }

    private sealed class FakeExternalLauncher : IExternalLauncher
    {
        public bool DesktopInstalled { get; set; }
        public List<Uri> OpenedUrls { get; } = [];

        public bool OpenHttpUrl(Uri uri)
        {
            OpenedUrls.Add(uri);
            return true;
        }

        public bool IsJellyfinDesktopInstalled() => DesktopInstalled;
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public ConnectionSessionState CurrentSession { get; } = new()
        {
            IsAuthenticated = true,
            ServerUri = new Uri("http://192.168.1.10:8096/"),
            ServerId = "srv001",
            UserId = "user1",
            Username = "demo",
            AccessToken = "secret-token-for-test"
        };

        public Task<ServerPublicInfo> TestConnectionAsync(string serverUrl, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ConnectionSessionState> LoginWithApiKeyAsync(string serverUrl, string apiKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ConnectionSessionState> LoginAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ConnectionSessionState> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentSession);

        public Task LogoutAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeJellyfinClient : IJellyfinClient
    {
        public List<JellyfinSessionInfo> Sessions { get; set; } = [];
        public List<(string SessionId, string ItemId, string ItemType, string? ItemName)> ViewingCalls { get; } = [];

        public Task<IReadOnlyList<JellyfinSessionInfo>> GetSessionsAsync(Uri serverUri, string accessToken, string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JellyfinSessionInfo>>(Sessions);

        public Task SendSessionViewingAsync(Uri serverUri, string sessionId, string itemId, string itemType, string? itemName, string accessToken, string deviceId, CancellationToken cancellationToken = default)
        {
            ViewingCalls.Add((sessionId, itemId, itemType, itemName));
            return Task.CompletedTask;
        }

        public Task<ServerPublicInfo> GetPublicServerInfoAsync(Uri serverUri, CancellationToken cancellationToken = default)
            => Task.FromResult(new ServerPublicInfo
            {
                ServerId = "srv001",
                ServerName = "TestServer",
                Version = "10.10.0"
            });

        public Task<AuthResult> AuthenticateByNameAsync(Uri serverUri, string username, string password, string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuthResult
            {
                UserId = "user1",
                Username = username,
                AccessToken = "secret-token-for-test",
                ServerId = "srv001"
            });

        public Task<bool> ValidateSessionAsync(Uri serverUri, string accessToken, string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ValidateApiKeyAsync(Uri serverUri, string apiKey, string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<JellyfinLibrary>> GetUserViewsAsync(Uri serverUri, string? userId, string accessToken, string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JellyfinLibrary>>([]);

        public Task<(IReadOnlyList<MediaItem> Items, int TotalRecordCount)> GetItemsAsync(
            Uri serverUri,
            string? userId,
            string accessToken,
            string deviceId,
            string? parentId,
            IReadOnlyList<string> includeItemTypes,
            int startIndex,
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<MediaItem> Items, int TotalRecordCount)>(([], 0));

        public Task<(byte[] Bytes, string? ContentType)> DownloadBackdropImageAsync(
            Uri serverUri,
            string itemId,
            int imageIndex,
            string? tag,
            int maxWidth,
            int quality,
            string accessToken,
            string deviceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<(byte[] Bytes, string? ContentType)>(([], "image/png"));

        public Task LogoutAsync(Uri serverUri, string accessToken, string deviceId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
