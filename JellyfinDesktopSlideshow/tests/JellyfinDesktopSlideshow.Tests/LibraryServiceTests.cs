using System.IO;
using System.Net;
using System.Net.Http;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class LibraryServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-lib-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetMediaItemsAsync_PaginatesAndDeduplicatesItems()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings.json");

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.0"}""");
            }

            if (path.Contains("/Users/AuthenticateByName", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"User":{"Id":"u-1","Name":"ali"},"AccessToken":"tok-1","ServerId":"srv-1"}""");
            }

            return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK,
                """
                {
                  "Items": [
                    {"Id":"m-1","Name":"Arrival","Type":"Movie","ProductionYear":2016,"BackdropImageTags":["tag1"]},
                    {"Id":"s-1","Name":"Dark","Type":"Series","ProductionYear":2017,"BackdropImageTags":[]}
                  ],
                  "TotalRecordCount": 2
                }
                """);
        });

        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, settingsPath);
        settings.Load();

        var client = new JellyfinClient(new HttpClient(handler), logger);
        var auth = new AuthenticationService(client, settings, new InMemorySecureStorageService(), logger);
        await auth.LoginAsync("http://192.168.1.10:8096", "ali", "1234");

        var libraryService = new LibraryService(client, auth, settings);
        var items = await libraryService.GetMediaItemsAsync(["lib-1", "lib-2"], ["Movie", "Series"]);

        Assert.Equal(2, items.Count);
        Assert.True(items.First(i => i.Id == "m-1").HasBackdrop);
        Assert.False(items.First(i => i.Id == "s-1").HasBackdrop);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
