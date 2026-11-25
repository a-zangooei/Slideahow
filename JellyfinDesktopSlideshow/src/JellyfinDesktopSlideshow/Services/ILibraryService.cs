using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface ILibraryService
{
    Task<IReadOnlyList<JellyfinLibrary>> GetAccessibleLibrariesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaItem>> GetMediaItemsAsync(
        IReadOnlyList<string> libraryIds,
        IReadOnlyList<string> includeItemTypes,
        CancellationToken cancellationToken = default);
}
