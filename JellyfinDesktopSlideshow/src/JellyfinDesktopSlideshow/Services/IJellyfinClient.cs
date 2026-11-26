using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface IJellyfinClient
{
    Task<ServerPublicInfo> GetPublicServerInfoAsync(Uri serverUri, CancellationToken cancellationToken = default);

    Task<AuthResult> AuthenticateByNameAsync(
        Uri serverUri,
        string username,
        string password,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateSessionAsync(
        Uri serverUri,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateApiKeyAsync(
        Uri serverUri,
        string apiKey,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JellyfinLibrary>> GetUserViewsAsync(
        Uri serverUri,
        string? userId,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MediaItem> Items, int TotalRecordCount)> GetItemsAsync(
        Uri serverUri,
        string? userId,
        string accessToken,
        string deviceId,
        string? parentId,
        IReadOnlyList<string> includeItemTypes,
        int startIndex,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaItem>> GetCollectionItemsAsync(
        Uri serverUri,
        string? userId,
        string accessToken,
        string deviceId,
        string collectionId,
        CancellationToken cancellationToken = default);

    Task<(byte[] Bytes, string? ContentType)> DownloadBackdropImageAsync(
        Uri serverUri,
        string itemId,
        int imageIndex,
        string? tag,
        int maxWidth,
        int quality,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JellyfinSessionInfo>> GetSessionsAsync(
        Uri serverUri,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task SendSessionViewingAsync(
        Uri serverUri,
        string sessionId,
        string itemId,
        string itemType,
        string? itemName,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        Uri serverUri,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default);
}
