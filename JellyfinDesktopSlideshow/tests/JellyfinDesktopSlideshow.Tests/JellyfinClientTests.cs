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
}
