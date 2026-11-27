using System.IO;
using System.Text;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class CacheService : ICacheService
{
    private readonly ILoggingService _logger;
    private readonly Lock _cacheLock = new();

    public string CacheDirectory { get; }

    public CacheService(ILoggingService logger, string? cacheDirectory = null)
    {
        _logger = logger;
        CacheDirectory = cacheDirectory ?? AppDataPaths.Default.BackdropCacheDirectory;
    }

    public string? TryGetCachedFile(string itemId, int imageIndex, string? tag)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        lock (_cacheLock)
        {
            if (!Directory.Exists(CacheDirectory))
            {
                return null;
            }

            var prefix = BuildFileStem(itemId, imageIndex, tag);
            var matches = Directory.GetFiles(CacheDirectory, $"{prefix}.*");

            foreach (var path in matches)
            {
                if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ImageFormatValidator.IsValidImageFile(path))
                {
                    TouchAccessTime(path);
                    return path;
                }

                TryDeleteFile(path);
            }

            return null;
        }
    }

    public string? TryGetAnyCachedFileForItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        lock (_cacheLock)
        {
            if (!Directory.Exists(CacheDirectory))
            {
                return null;
            }

            var safeId = SanitizeFilePart(itemId);
            var matches = Directory.GetFiles(CacheDirectory, $"{safeId}_*.*")
                .Where(p => !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc);

            foreach (var path in matches)
            {
                if (ImageFormatValidator.IsValidImageFile(path))
                {
                    TouchAccessTime(path);
                    return path;
                }
            }

            return null;
        }
    }

    public async Task<string?> SaveToCacheAsync(
        string itemId,
        int imageIndex,
        string? tag,
        byte[] imageBytes,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(imageBytes);

        var normalizedExt = extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
        var fileName = $"{BuildFileStem(itemId, imageIndex, tag)}{normalizedExt}";
        var targetPath = Path.Combine(CacheDirectory, fileName);
        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(tempPath, imageBytes, cancellationToken);

            lock (_cacheLock)
            {
                File.Move(tempPath, targetPath, overwrite: true);
            }

            return targetPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"ذخیرهٔ تصویر در کش ناموفق بود (احتمالاً کمبود فضای دیسک یا خطای دسترسی): {ex.Message}");
            TryDeleteFile(tempPath);
            return null;
        }
    }

    public void PruneCache(int maxCacheSizeMb, int maxCacheAgeDays)
    {
        lock (_cacheLock)
        {
            if (!Directory.Exists(CacheDirectory))
            {
                return;
            }

            try
            {
                var files = new DirectoryInfo(CacheDirectory)
                    .GetFiles()
                    .Where(f => !f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, maxCacheAgeDays));
                var remaining = new List<FileInfo>();

                foreach (var file in files)
                {
                    if (file.LastWriteTimeUtc < cutoffUtc)
                    {
                        TryDeleteFile(file.FullName);
                    }
                    else
                    {
                        remaining.Add(file);
                    }
                }

                var maxBytes = Math.Max(16L, maxCacheSizeMb) * 1024L * 1024L;
                var totalBytes = remaining.Sum(f => f.Length);
                if (totalBytes <= maxBytes)
                {
                    return;
                }

                foreach (var oldest in remaining.OrderBy(f => f.LastAccessTimeUtc))
                {
                    if (totalBytes <= maxBytes)
                    {
                        break;
                    }

                    var len = oldest.Length;
                    if (TryDeleteFile(oldest.FullName))
                    {
                        totalBytes -= len;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"خطا هنگام پاک‌سازی خودکار کش: {ex.Message}");
            }
        }
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            if (!Directory.Exists(CacheDirectory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(CacheDirectory))
            {
                TryDeleteFile(file);
            }
        }
    }

    public CacheStats GetStats()
    {
        lock (_cacheLock)
        {
            if (!Directory.Exists(CacheDirectory))
            {
                return new CacheStats { FileCount = 0, TotalBytes = 0 };
            }

            var files = new DirectoryInfo(CacheDirectory)
                .GetFiles()
                .Where(f => !f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new CacheStats
            {
                FileCount = files.Count,
                TotalBytes = files.Sum(f => f.Length)
            };
        }
    }

    private static string BuildFileStem(string itemId, int imageIndex, string? tag)
    {
        var safeId = SanitizeFilePart(itemId);
        var safeTag = string.IsNullOrWhiteSpace(tag) ? "notag" : SanitizeFilePart(tag);
        return $"{safeId}_{imageIndex}_{safeTag}";
    }

    private static string SanitizeFilePart(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }

        return sb.Length > 0 ? sb.ToString() : "item";
    }

    private static void TouchAccessTime(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Non-fatal
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
