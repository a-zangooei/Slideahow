using System.IO;
using System.Text.Json;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class CurrentWallpaperItemStore : ICurrentWallpaperItemStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storeFilePath;
    private readonly ILoggingService _logger;
    private readonly object _syncRoot = new();

    public CurrentWallpaperItemStore(ILoggingService logger, string? storeFilePath = null)
    {
        _logger = logger;
        _storeFilePath = storeFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JellyfinDesktopSlideshow",
            "current-wallpaper-item.json");
    }

    public MediaItem? Load()
    {
        lock (_syncRoot)
        {
            try
            {
                if (!File.Exists(_storeFilePath))
                {
                    return null;
                }

                var json = File.ReadAllText(_storeFilePath);
                var dto = JsonSerializer.Deserialize<PersistedWallpaperItemDto>(json, JsonOptions);
                if (dto is null ||
                    string.IsNullOrWhiteSpace(dto.Id) ||
                    string.IsNullOrWhiteSpace(dto.Name))
                {
                    _logger.Warning("اطلاعات ذخیره‌شدهٔ عنوان والپیپر فعلی ناقص یا نامعتبر است.");
                    return null;
                }

                return new MediaItem
                {
                    Id = dto.Id.Trim(),
                    Name = dto.Name.Trim(),
                    Type = string.IsNullOrWhiteSpace(dto.Type) ? "Movie" : dto.Type.Trim(),
                    ProductionYear = dto.ProductionYear,
                    BackdropImageTags = dto.BackdropImageTags ?? []
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"خواندن اطلاعات عنوان والپیپر فعلی ناموفق بود: {ex.Message}");
                return null;
            }
        }
    }

    public void Save(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        lock (_syncRoot)
        {
            string? tempPath = null;
            try
            {
                var directory = Path.GetDirectoryName(_storeFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var dto = new PersistedWallpaperItemDto
                {
                    Id = item.Id.Trim(),
                    Name = item.Name.Trim(),
                    Type = item.Type,
                    ProductionYear = item.ProductionYear,
                    BackdropImageTags = item.BackdropImageTags.ToList(),
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                tempPath = $"{_storeFilePath}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _storeFilePath, overwrite: true);
                tempPath = null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"ذخیرهٔ اطلاعات عنوان والپیپر فعلی ناموفق بود: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Ignore best-effort temp cleanup.
                    }
                }
            }
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            try
            {
                if (File.Exists(_storeFilePath))
                {
                    File.Delete(_storeFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"پاک‌سازی اطلاعات عنوان والپیپر فعلی ناموفق بود: {ex.Message}");
            }
        }
    }

    private sealed class PersistedWallpaperItemDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public int? ProductionYear { get; set; }
        public List<string>? BackdropImageTags { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
