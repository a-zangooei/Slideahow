using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class LibraryService : ILibraryService
{
    private const int PageSize = 200;

    private static readonly HashSet<string> SupportedCollectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "movies",
        "tvshows",
        "mixed",
        "boxsets"
    };

    private readonly IJellyfinClient _client;
    private readonly IAuthenticationService _authService;
    private readonly ISettingsService _settingsService;

    public LibraryService(
        IJellyfinClient client,
        IAuthenticationService authService,
        ISettingsService settingsService)
    {
        _client = client;
        _authService = authService;
        _settingsService = settingsService;
    }

    public async Task<IReadOnlyList<JellyfinLibrary>> GetAccessibleLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var session = EnsureAuthenticatedSession();
        var views = await _client.GetUserViewsAsync(
            session.ServerUri!,
            session.UserId!,
            session.AccessToken!,
            _settingsService.Current.DeviceId,
            cancellationToken);

        return views
            .Where(v => string.IsNullOrWhiteSpace(v.CollectionType) || SupportedCollectionTypes.Contains(v.CollectionType))
            .ToList();
    }

    public async Task<IReadOnlyList<MediaItem>> GetMediaItemsAsync(
        IReadOnlyList<string> libraryIds,
        IReadOnlyList<string> includeItemTypes,
        CancellationToken cancellationToken = default)
    {
        var session = EnsureAuthenticatedSession();
        var types = includeItemTypes.Count > 0 ? includeItemTypes : ["Movie", "Series"];
        var parentIds = libraryIds.Count > 0 ? libraryIds : [string.Empty];

        var uniqueItems = new Dictionary<string, MediaItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var parentId in parentIds)
        {
            var startIndex = 0;
            while (true)
            {
                var (batch, totalCount) = await _client.GetItemsAsync(
                    session.ServerUri!,
                    session.UserId!,
                    session.AccessToken!,
                    _settingsService.Current.DeviceId,
                    string.IsNullOrWhiteSpace(parentId) ? null : parentId,
                    types,
                    startIndex,
                    PageSize,
                    cancellationToken);

                foreach (var item in batch)
                {
                    uniqueItems.TryAdd(item.Id, item);
                }

                startIndex += batch.Count;
                if (batch.Count == 0 || startIndex >= totalCount)
                {
                    break;
                }
            }
        }

        return uniqueItems.Values
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private ConnectionSessionState EnsureAuthenticatedSession()
    {
        var session = _authService.CurrentSession;
        if (!session.IsAuthenticated ||
            session.ServerUri is null ||
            string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.AccessToken))
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.Unauthorized,
                "ابتدا وارد حساب کاربری Jellyfin شوید.");
        }

        return session;
    }
}
