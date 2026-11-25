using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface IImageService
{
    Task<BackdropResolveResult> ResolveBackdropAsync(
        MediaItem item,
        CancellationToken cancellationToken = default);
}
