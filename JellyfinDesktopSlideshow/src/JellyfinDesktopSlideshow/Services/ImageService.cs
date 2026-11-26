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

        var candidateIndices = BuildCandidateIndices(item.BackdropImageTags.Count, settings);
        var rejectedByAspectCount = 0;
        string? lastAspectReason = null;
        string? lastFailureReason = null;
        JellyfinImageInfo? lastImageInfo = null;
        var networkErrorOccurred = false;
        var unauthenticated = false;

        foreach (var imageIndex in candidateIndices)
        {
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
            lastImageInfo = imageInfo;

            var cachedPath = _cacheService.TryGetCachedFile(item.Id, imageIndex, tag);
            if (cachedPath is not null)
            {
                if (ImageAspectFilter.IsAcceptableFile(cachedPath, settings, out var cachedAspectReason))
                {
                    return new BackdropResolveResult
                    {
                        Status = BackdropResolveStatus.Success,
                        LocalFilePath = cachedPath,
                        ImageInfo = imageInfo,
                        Message = "تصویر از کش محلی بارگذاری شد."
                    };
                }

                rejectedByAspectCount++;
                lastAspectReason = cachedAspectReason;
                _logger.Info($"Backdrop شماره {imageIndex} عنوان «{item.Name}» ({item.Id}) در کش به‌دلیل نسبت نامناسب رد شد: {cachedAspectReason}");
                continue;
            }

            var session = _authService.CurrentSession;
            if (!session.IsAuthenticated ||
                session.ServerUri is null ||
                string.IsNullOrWhiteSpace(session.AccessToken))
            {
                unauthenticated = true;
                lastFailureReason = "اتصال فعال برقرار نیست.";
                break;
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
                    _logger.Warning($"تصویر دریافت‌شده برای {item.Name} ({item.Id}) در Backdrop شماره {imageIndex} ناقص یا نامعتبر بود.");
                    lastFailureReason = "تصویر دریافت‌شده از سرور خراب یا ناقص است.";
                    continue;
                }

                if (!ImageAspectFilter.IsAcceptableBytes(bytes, settings, out var aspectReason))
                {
                    rejectedByAspectCount++;
                    lastAspectReason = aspectReason;
                    _logger.Info($"Backdrop شماره {imageIndex} عنوان «{item.Name}» ({item.Id}) به‌دلیل نسبت تصویر نامناسب رد شد: {aspectReason}");
                    continue;
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
                    lastFailureReason = "فضای دیسک کافی نیست یا نوشتن در پوشهٔ کش امکان‌پذیر نشد.";
                    continue;
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
                networkErrorOccurred = true;
                lastFailureReason = ex.Message;
                break;
            }
        }

        if (rejectedByAspectCount == candidateIndices.Count)
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.SkippedNoBackdrop,
                ImageInfo = lastImageInfo,
                Message = lastAspectReason is null
                    ? $"هیچ‌یک از تصاویر Backdrop عنوان «{item.Name}» نسبت مناسب والپیپر دسکتاپ نداشتند."
                    : $"عنوان «{item.Name}» رد شد: {lastAspectReason}"
            };
        }

        var fallbackImageInfo = lastImageInfo ?? new JellyfinImageInfo
        {
            ItemId = item.Id,
            ImageIndex = candidateIndices[0],
            Tag = item.BackdropImageTags[candidateIndices[0]],
            MaxWidth = DefaultMaxWidth,
            Quality = DefaultQuality,
            RequestUrl = item.BackdropImageUrl
        };

        if (unauthenticated || networkErrorOccurred)
        {
            return TryOfflineCacheOrFallback(
                item,
                fallbackImageInfo,
                settings,
                lastFailureReason ?? "اتصال فعال برقرار نیست.");
        }

        return TryFallbackOrFailure(
            fallbackImageInfo,
            settings,
            lastFailureReason ?? "دریافت تصویر مناسب برای این عنوان ناموفق بود.");
    }

    private static List<int> BuildCandidateIndices(int availableCount, AppSettings settings)
    {
        var primaryIndex = SelectBackdropIndex(availableCount, settings);
        var indices = new List<int>(availableCount) { primaryIndex };
        for (var i = 0; i < availableCount; i++)
        {
            if (i != primaryIndex)
            {
                indices.Add(i);
            }
        }

        return indices;
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
        if (offlineCached is not null &&
            ImageAspectFilter.IsAcceptableFile(offlineCached, settings, out _))
        {
            return new BackdropResolveResult
            {
                Status = BackdropResolveStatus.UsedCacheOffline,
                LocalFilePath = offlineCached,
                ImageInfo = imageInfo,
                Message = "سرور در دسترس نیست؛ نسخهٔ کش‌شده نمایش داده شد."
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
