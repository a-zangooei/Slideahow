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
    private readonly Localization.ILocalizationService _localization;

    public LibraryService(
        IJellyfinClient client,
        IAuthenticationService authService,
        ISettingsService settingsService,
        Localization.ILocalizationService? localization = null)
    {
        _client = client;
        _authService = authService;
        _settingsService = settingsService;
        _localization = localization ?? Localization.AppLocalization.Current;
    }

    public async Task<IReadOnlyList<JellyfinLibrary>> GetAccessibleLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var session = EnsureAuthenticatedSession();
        var views = await _client.GetUserViewsAsync(
            session.ServerUri!,
            session.UserId,
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
        var effectiveTypes = types.Any(t => string.Equals(t, "BoxSet", StringComparison.OrdinalIgnoreCase))
            ? types
            : [.. types, "BoxSet"];
        var parentIds = libraryIds.Count > 0 ? libraryIds : [string.Empty];

        var uniqueItems = new Dictionary<string, MediaItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var parentId in parentIds)
        {
            var startIndex = 0;
            while (true)
            {
                var (batch, totalCount) = await _client.GetItemsAsync(
                    session.ServerUri!,
                    session.UserId,
                    session.AccessToken!,
                    _settingsService.Current.DeviceId,
                    string.IsNullOrWhiteSpace(parentId) ? null : parentId,
                    effectiveTypes,
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

        var resolvedItems = new List<MediaItem>(uniqueItems.Count);
        foreach (var item in uniqueItems.Values)
        {
            if (!item.IsCollection)
            {
                resolvedItems.Add(item);
                continue;
            }

            var visitedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.Id };
            var members = await TryResolveCollectionMembersAsync(session, item.Id, visitedIds, depth: 0, cancellationToken);
            resolvedItems.Add(members.Count > 0
                ? new MediaItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = item.Type,
                    ProductionYear = item.ProductionYear,
                    BackdropImageUrl = item.BackdropImageUrl,
                    BackdropImageTags = item.BackdropImageTags,
                    CollectionMembers = members
                }
                : item);
        }

        return resolvedItems
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<MediaItem>> TryResolveCollectionMembersAsync(
        ConnectionSessionState session,
        string collectionId,
        HashSet<string> visitedIds,
        int depth,
        CancellationToken cancellationToken)
    {
        const int maxCollectionDepth = 3;
        if (depth >= maxCollectionDepth)
        {
            return [];
        }

        IReadOnlyList<MediaItem> rawMembers;
        try
        {
            rawMembers = await _client.GetCollectionItemsAsync(
                session.ServerUri!,
                session.UserId,
                session.AccessToken!,
                _settingsService.Current.DeviceId,
                collectionId,
                cancellationToken);
        }
        catch (JellyfinApiException ex) when (ex.ErrorKind is not JellyfinErrorKind.Unauthorized and not JellyfinErrorKind.Forbidden)
        {
            return [];
        }

        var members = new List<MediaItem>(rawMembers.Count);
        foreach (var member in rawMembers)
        {
            if (!visitedIds.Add(member.Id))
            {
                continue;
            }

            if (member.IsCollection)
            {
                var nestedMembers = await TryResolveCollectionMembersAsync(session, member.Id, visitedIds, depth + 1, cancellationToken);
                members.Add(nestedMembers.Count > 0
                    ? new MediaItem
                    {
                        Id = member.Id,
                        Name = member.Name,
                        Type = member.Type,
                        ProductionYear = member.ProductionYear,
                        BackdropImageUrl = member.BackdropImageUrl,
                        BackdropImageTags = member.BackdropImageTags,
                        CollectionMembers = nestedMembers
                    }
                    : member);
            }
            else
            {
                members.Add(member);
            }
        }

        return members;
    }

    private ConnectionSessionState EnsureAuthenticatedSession()
    {
        var session = _authService.CurrentSession;
        if (!session.IsAuthenticated ||
            session.ServerUri is null ||
            string.IsNullOrWhiteSpace(session.AccessToken) ||
            (!session.IsApiKeySession && string.IsNullOrWhiteSpace(session.UserId)))
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.Unauthorized,
                _localization.GetString("Auth.Error.AuthRequired"));
        }

        return session;
    }
}
