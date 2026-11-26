using System.Net;
using System.Net.Http;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class JellyfinClientTests
{
    [Fact]
    public async Task GetPublicServerInfoAsync_ReturnsServerMetadata()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """{"Id":"srv-1","ServerName":"HomeMedia","Version":"10.10.3","StartupWizardCompleted":true}"""));

        var client = new JellyfinClient(new HttpClient(handler), new NullLoggingService());
        var info = await client.GetPublicServerInfoAsync(new Uri("http://192.168.1.10:8096/"));

        Assert.Equal("srv-1", info.ServerId);
        Assert.Equal("HomeMedia", info.ServerName);
        Assert.Equal("10.10.3", info.Version);
        Assert.Equal("/System/Info/Public", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AuthenticateByNameAsync_SendsMediaBrowserHeaderAndParsesToken()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """{"User":{"Id":"u-1","Name":"ali"},"AccessToken":"tok-xyz","ServerId":"srv-1"}"""));

        var client = new JellyfinClient(new HttpClient(handler), new NullLoggingService());
        var result = await client.AuthenticateByNameAsync(new Uri("https://media.example.com/"), "ali", "pass", "dev-1");

        Assert.Equal("u-1", result.UserId);
        Assert.Equal("tok-xyz", result.AccessToken);

        var authHeader = handler.Requests[0].Headers.Authorization!;
        Assert.Equal("MediaBrowser", authHeader.Scheme);
        Assert.Contains("DeviceId=\"dev-1\"", authHeader.Parameter);
    }

    [Fact]
    public async Task GetUserViewsAsync_FallsBackToUserViewsRouteOn404()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/Users/u-1/Views", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """{"Items":[{"Id":"lib-1","Name":"Movies","CollectionType":"movies"}],"TotalRecordCount":1}""");
        });

        var client = new JellyfinClient(new HttpClient(handler), new NullLoggingService());
        var views = await client.GetUserViewsAsync(new Uri("http://localhost:8096/"), "u-1", "tok", "dev-1");

        Assert.Single(views);
        Assert.Equal("Movies", views[0].Name);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetItemsAsync_ThrowsUnreachableOnNetworkError()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var client = new JellyfinClient(new HttpClient(handler), new NullLoggingService());

        var ex = await Assert.ThrowsAsync<JellyfinApiException>(() =>
            client.GetItemsAsync(new Uri("http://192.168.1.99:8096/"), "u-1", "tok", "dev-1", null, ["Movie"], 0, 50));

        Assert.Equal(JellyfinErrorKind.Unreachable, ex.ErrorKind);
    }

    [Fact]
    public async Task ValidateApiKeyAsync_UsesSystemInfoWithMediaBrowserTokenHeader()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Headers.Authorization?.Parameter?.Contains("Token=\"bad-key\"", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return StubHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"Id":"srv-1","Version":"10.10.3"}""");
        });

        var client = new JellyfinClient(new HttpClient(handler), new NullLoggingService());

        var valid = await client.ValidateApiKeyAsync(new Uri("https://media.example.com/"), "good-key", "dev-1");
        var invalid = await client.ValidateApiKeyAsync(new Uri("https://media.example.com/"), "bad-key", "dev-1");

        Assert.True(valid);
        Assert.False(invalid);
        Assert.Equal("/System/Info", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("MediaBrowser", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Contains("Token=\"good-key\"", handler.Requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetUserViewsAndItems_WithoutUserId_UseMediaFoldersAndRootItemsEndpoints()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/Library/MediaFolders", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """{"Items":[{"Id":"lib-movies","Name":"Movies","CollectionType":"movies"}],"TotalRecordCount":1}""");
            }

            return StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """{"Items":[{"Id":"m-1","Name":"Dune","Type":"Movie","BackdropImageTags":["t1"]}],"TotalRecordCount":1}""");
        });

        var client = new JellyfinClient(new HttpClient(handler), new NullLoggingService());

        var libs = await client.GetUserViewsAsync(new Uri("http://localhost:8096/"), null, "api-key-1", "dev-1");
        var (items, total) = await client.GetItemsAsync(new Uri("http://localhost:8096/"), null, "api-key-1", "dev-1", "lib-movies", ["Movie"], 0, 50);

        Assert.Single(libs);
        Assert.Equal("lib-movies", libs[0].Id);
        Assert.Single(items);
        Assert.Equal(1, total);
        Assert.Equal("/Library/MediaFolders", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/Items", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.DoesNotContain("api-key-1", handler.Requests[1].RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCollectionItemsAsync_UsesCollapseBoxSetItemsFalseAndFiltersSelfOrEpisodes()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var query = req.RequestUri!.Query;
            Assert.Contains("ParentId=col-1", query, StringComparison.Ordinal);
            Assert.Contains("Recursive=true", query, StringComparison.Ordinal);
            Assert.Contains("CollapseBoxSetItems=false", query, StringComparison.Ordinal);

            return StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "Items": [
                    {"Id":"col-1","Name":"Self Collection","Type":"BoxSet","BackdropImageTags":["t-col"]},
                    {"Id":"m-1","Name":"Movie 1","Type":"Movie","BackdropImageTags":["t-m1"]},
                    {"Id":"m-1","Name":"Movie 1 Duplicate","Type":"Movie","BackdropImageTags":["t-m1"]},
                    {"Id":"ep-1","Name":"Episode 1","Type":"Episode","BackdropImageTags":["t-ep"]},
                    {"Id":"s-1","Name":"Series 1","Type":"Series","BackdropImageTags":["t-s1"]}
                  ],
                  "TotalRecordCount": 5
                }
                """);
        });

        var client = new JellyfinClient(new HttpClient(handler), new NullLoggingService());

        var userMembers = await client.GetCollectionItemsAsync(
            new Uri("http://localhost:8096/"),
            "u-1",
            "user-tok",
            "dev-1",
            "col-1");

        var apiKeyMembers = await client.GetCollectionItemsAsync(
            new Uri("http://localhost:8096/"),
            null,
            "api-key-1",
            "dev-1",
            "col-1");

        Assert.Equal("/Users/u-1/Items", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/Items", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(2, userMembers.Count);
        Assert.Equal(["m-1", "s-1"], userMembers.Select(m => m.Id).ToArray());
        Assert.Equal(2, apiKeyMembers.Count);
    }
}
