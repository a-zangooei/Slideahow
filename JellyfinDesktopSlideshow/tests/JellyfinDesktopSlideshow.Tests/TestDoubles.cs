using System.Net;
using System.Net.Http;
using System.Text;
using JellyfinDesktopSlideshow.Infrastructure;

namespace JellyfinDesktopSlideshow.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_handler(request));
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

internal sealed class InMemorySecureStorageService : ISecureStorageService
{
    public Dictionary<string, (string Username, string Secret)> Store { get; } = new();

    public void SaveSecret(string key, string username, string secret) => Store[key] = (username, secret);

    public string? ReadSecret(string key) => Store.TryGetValue(key, out var entry) ? entry.Secret : null;

    public void DeleteSecret(string key) => Store.Remove(key);
}

internal sealed class NullLoggingService : ILoggingService
{
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
