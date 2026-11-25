using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface ICacheService
{
    string CacheDirectory { get; }
    string? TryGetCachedFile(string itemId, int imageIndex, string? tag);
    string? TryGetAnyCachedFileForItem(string itemId);
    Task<string?> SaveToCacheAsync(
        string itemId,
        int imageIndex,
        string? tag,
        byte[] imageBytes,
        string extension,
        CancellationToken cancellationToken = default);
    void PruneCache(int maxCacheSizeMb, int maxCacheAgeDays);
    void ClearCache();
    CacheStats GetStats();
}
