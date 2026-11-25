using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class ConnectionSessionState
{
    public bool IsAuthenticated { get; init; }
    public Uri? ServerUri { get; init; }
    public string? ServerName { get; init; }
    public string? ServerVersion { get; init; }
    public string? ServerId { get; init; }
    public string? UserId { get; init; }
    public string? Username { get; init; }
    public string? AccessToken { get; init; }
    public bool IsNonLocalPlainHttp { get; init; }
    public string? WarningOrError { get; init; }
}

public interface IAuthenticationService
{
    ConnectionSessionState CurrentSession { get; }

    Task<ServerPublicInfo> TestConnectionAsync(string serverUrl, CancellationToken cancellationToken = default);

    Task<ConnectionSessionState> LoginAsync(
        string serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<ConnectionSessionState> TryRestoreSessionAsync(CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
