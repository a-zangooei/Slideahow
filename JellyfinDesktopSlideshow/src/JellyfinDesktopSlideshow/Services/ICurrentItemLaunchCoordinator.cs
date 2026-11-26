using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface ICurrentItemLaunchCoordinator
{
    MediaItem? ResolveCurrentItem(MediaItem? preferredFallbackItem = null);
    void RecordDisplayedItem(MediaItem? item);
    Task<ItemLaunchResult> LaunchCurrentItemAsync(MediaItem? preferredFallbackItem = null, CancellationToken cancellationToken = default);
}
