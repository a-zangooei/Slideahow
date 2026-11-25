using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class ImageService : IImageService
{
    private const int DefaultMaxWidth = 1920;
    private const int DefaultQuality = 92;

    private readonly IJellyfinClient _client;
    private readonly IAuthenticationService _authService;
    private readonly ICacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;

    public ImageService(
        IJellyfinClient client,
        IAuthenticationService authService,
        ICacheService cacheService,
        ISettingsService settingsService,
        ILoggingService logger)
    {
        _client = client;
        _authService = authService;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<BackdropResolveResult> ResolveBackdropAsync(
        MediaItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var settings = _settingsService.Current;
        if (item.BackdropImageTags.Count == 0)
        {
            return ResolveWithoutBackdrop(item, settings);
        }

        var imageIndex = SelectBackdropIndex(item.BackdropImageTags.Count, settings);
        var tag = item.BackdropImageTags[imageIndex];
        var imageInfo = new JellyfinImageInfo
        {
            ItemId = item.Id,
            ImageIndex = imageIndex,
            Tag = tag,
            MaxWidth = DefaultMaxWidth,
            Quality = DefaultQuality,
            RequestUrl = item.BackdropImageUrl
        };

        var cachedPath = _cacheService.TryGetCachedFile(item.Id, imageIndex, tag);
        if (cachedPath is not null)
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.Success,
                LocalFilePath = cachedPath,
                ImageInfo = imageInfo,
                Message = "تصویر از کش محلی بارگذاری شد."
            };
        }

        var session = _authService.CurrentSession;
        if (!session.IsAuthenticated ||
            session.ServerUri is null ||
            string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return TryOfflineCacheOrFallback(item, imageInfo, settings, "اتصال فعال برقرار نیست.");
        }

        try
        {
            var (bytes, _) = await _client.DownloadBackdropImageAsync(
                session.ServerUri,
                item.Id,
                imageIndex,
                tag,
                DefaultMaxWidth,
                DefaultQuality,
                session.AccessToken,
                settings.DeviceId,
                cancellationToken);

            if (!ImageFormatValidator.TryDetectImageExtension(bytes, out var extension))
            {
                _logger.Warning($"تصویر دریافت‌شده برای {item.Name} ({item.Id}) ناقص یا نامعتبر بود.");
                return TryFallbackOrFailure(
                    imageInfo,
                    settings,
                    "تصویر دریافت‌شده از سرور خراب یا ناقص است.");
            }

            var savedPath = await _cacheService.SaveToCacheAsync(
                item.Id,
                imageIndex,
                tag,
                bytes,
                extension,
                cancellationToken);

            if (savedPath is null)
            {
                return TryFallbackOrFailure(
                    imageInfo,
                    settings,
                    "فضای دیسک کافی نیست یا نوشتن در پوشهٔ کش امکان‌پذیر نشد.");
            }

            _cacheService.PruneCache(settings.MaxCacheSizeMb, settings.MaxCacheAgeDays);

            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.Success,
                LocalFilePath = savedPath,
                ImageInfo = imageInfo,
                Message = "تصویر Backdrop دریافت و در کش ذخیره شد."
            };
        }
        catch (JellyfinApiException ex)
        {
            _logger.Warning($"خطا در دریافت Backdrop برای {item.Name}: {ex.Message}");
            return TryOfflineCacheOrFallback(item, imageInfo, settings, ex.Message);
        }
    }

    private static int SelectBackdropIndex(int availableCount, AppSettings settings)
    {
        if (availableCount <= 1)
        {
            return 0;
        }

        if (settings.RandomBackdropPerItem)
        {
            return Random.Shared.Next(availableCount);
        }

        return Math.Clamp(settings.BackdropIndexPreference, 0, availableCount - 1);
    }

    private BackdropResolveResult ResolveWithoutBackdrop(MediaItem item, AppSettings settings)
    {
        if (ImageFormatValidator.IsValidImageFile(settings.FallbackImagePath))
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.UsedFallback,
                LocalFilePath = settings.FallbackImagePath,
                Message = $"عنوان «{item.Name}» Backdrop ندارد؛ تصویر جایگزین نمایش داده شد."
            };
        }

        return new BackdropResolveResult
        {
            Status = BackdropResolveStatus.SkippedNoBackdrop,
            Message = $"عنوان «{item.Name}» تصویر Backdrop ندارد و تصویر جایگزینی تنظیم نشده است."
        };
    }

    private BackdropResolveResult TryOfflineCacheOrFallback(
        MediaItem item,
        JellyfinImageInfo imageInfo,
        AppSettings settings,
        string reason)
    {
        var offlineCached = _cacheService.TryGetAnyCachedFileForItem(item.Id);
        if (offlineCached is not null)
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.UsedCacheOffline,
                LocalFilePath = offlineCached,
                ImageInfo = imageInfo,
                Message = $"سرور در دسترس نیست؛ نسخهٔ کش‌شده نمایش داده شد."
            };
        }

        return TryFallbackOrFailure(imageInfo, settings, reason);
    }

    private static BackdropResolveResult TryFallbackOrFailure(
        JellyfinImageInfo imageInfo,
        AppSettings settings,
        string reason)
    {
        if (ImageFormatValidator.IsValidImageFile(settings.FallbackImagePath))
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.UsedFallback,
                LocalFilePath = settings.FallbackImagePath,
                ImageInfo = imageInfo,
                Message = $"{reason} تصویر جایگزین نمایش داده شد."
            };
        }

        return new BackdropResolveResult
        {
            Status = BackdropResolveStatus.FailedCorruptOrIo,
            ImageInfo = imageInfo,
            Message = reason
        };
    }
}
