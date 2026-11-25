using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed record ItemLaunchResult(
    bool Succeeded,
    string Message,
    bool CanFallbackToBrowser = false,
    string? FallbackBrowserUrl = null,
    string? LaunchedUrl = null);

public interface IItemLauncher
{
    Task<ItemLaunchResult> OpenItemAsync(MediaItem item, CancellationToken cancellationToken = default);
    bool OpenValidatedBrowserUrl(string url);
    bool TryValidateCustomUrlTemplate(string? template, out string? errorMessage);
    Uri BuildWebDetailsUri(Uri serverUri, string itemId, string? serverId);
}
