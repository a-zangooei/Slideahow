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

    [Fact]
    public async Task LibraryService_InApiKeyMode_LoadsMediaFoldersAndItemsWithoutUserId()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings-apikey.json");

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.3"}""");
            }

            if (path.EndsWith("/System/Info", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","Version":"10.10.3"}""");
            }

            if (path.EndsWith("/Library/MediaFolders", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Items":[{"Id":"lib-1","Name":"Movies","CollectionType":"movies"}],"TotalRecordCount":1}""");
            }

            if (path.EndsWith("/Items", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Items":[{"Id":"m-1","Name":"Inception","Type":"Movie","BackdropImageTags":["t1"]}],"TotalRecordCount":1}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, settingsPath);
        settings.Load();

        var client = new JellyfinClient(new HttpClient(handler), logger);
        var auth = new AuthenticationService(client, settings, new InMemorySecureStorageService(), logger);
        await auth.LoginWithApiKeyAsync("http://192.168.1.10:8096", "api-key-xyz");

        var libraryService = new LibraryService(client, auth, settings);
        var libs = await libraryService.GetAccessibleLibrariesAsync();
        var items = await libraryService.GetMediaItemsAsync([libs[0].Id], ["Movie"]);

        Assert.Single(libs);
        Assert.Single(items);
        Assert.Equal("Inception", items[0].Name);
    }

    [Fact]
    public async Task LibraryService_SupportsBoxSetsFromCollectionsLibraryAndCollapsedMoviesLibrary()
    {
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings-boxsets.json");

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var query = req.RequestUri.Query;

            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","ServerName":"NAS","Version":"10.10.3"}""");
            }

            if (path.EndsWith("/System/Info", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","Version":"10.10.3"}""");
            }

            if (path.EndsWith("/Library/MediaFolders", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "Items": [
                        {"Id":"lib-movies","Name":"Movies","CollectionType":"movies"},
                        {"Id":"lib-collections","Name":"Collections","CollectionType":"boxsets"}
                      ],
                      "TotalRecordCount": 2
                    }
                    """);
            }

            if (path.EndsWith("/Items", StringComparison.Ordinal))
            {
                if (query.Contains("ParentId=col-lotr", StringComparison.Ordinal))
                {
                    Assert.Contains("CollapseBoxSetItems=false", query, StringComparison.Ordinal);
                    return StubHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "Items": [
                            {"Id":"m-lotr-1","Name":"Fellowship","Type":"Movie","BackdropImageTags":["tag-f"]},
                            {"Id":"m-lotr-2","Name":"Two Towers","Type":"Movie","BackdropImageTags":["tag-t"]}
                          ],
                          "TotalRecordCount": 2
                        }
                        """);
                }

                if (query.Contains("ParentId=col-empty", StringComparison.Ordinal))
                {
                    return StubHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "Items": [
                            {"Id":"m-empty-1","Name":"No Art Movie","Type":"Movie","BackdropImageTags":[]}
                          ],
                          "TotalRecordCount": 1
                        }
                        """);
                }

                Assert.Contains("BoxSet", query, StringComparison.Ordinal);

                if (query.Contains("ParentId=lib-movies", StringComparison.Ordinal))
                {
                    return StubHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "Items": [
                            {"Id":"col-lotr","Name":"LOTR Collection","Type":"BoxSet","BackdropImageTags":[]},
                            {"Id":"m-single","Name":"Matrix","Type":"Movie","BackdropImageTags":["tag-mx"]}
                          ],
                          "TotalRecordCount": 2
                        }
                        """);
                }

                if (query.Contains("ParentId=lib-collections", StringComparison.Ordinal))
                {
                    return StubHttpMessageHandler.JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "Items": [
                            {"Id":"col-lotr","Name":"LOTR Collection","Type":"BoxSet","BackdropImageTags":[]},
                            {"Id":"col-empty","Name":"Empty Art Collection","Type":"BoxSet","BackdropImageTags":[]}
                          ],
                          "TotalRecordCount": 2
                        }
                        """);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, settingsPath);
        settings.Load();

        var client = new JellyfinClient(new HttpClient(handler), logger);
        var auth = new AuthenticationService(client, settings, new InMemorySecureStorageService(), logger);
        await auth.LoginWithApiKeyAsync("http://192.168.1.10:8096", "api-key-xyz");

        var libraryService = new LibraryService(client, auth, settings);
        var libs = await libraryService.GetAccessibleLibrariesAsync();
        Assert.Equal(2, libs.Count);

        var items = await libraryService.GetMediaItemsAsync(["lib-movies", "lib-collections"], ["Movie", "Series"]);

        Assert.Equal(3, items.Count);
        var lotr = items.Single(i => i.Id == "col-lotr");
        Assert.True(lotr.IsCollection);
        Assert.True(lotr.HasBackdrop);
        Assert.Equal(2, lotr.CollectionMembers.Count);

        var emptyCol = items.Single(i => i.Id == "col-empty");
        Assert.True(emptyCol.IsCollection);
        Assert.False(emptyCol.HasBackdrop);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
