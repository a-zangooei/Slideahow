using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class CurrentItemLaunchCoordinator : ICurrentItemLaunchCoordinator
{
    public const string MissingItemErrorMessage =
        "اطلاعات عنوان متناظر با والپیپر فعلی در دسترس نیست. لطفاً برنامه را باز کنید و اسلایدشو را شروع نمایید یا یک عنوان را روی والپیپر اعمال کنید.";

    private readonly ISlideshowService? _slideshowService;
    private readonly ICurrentWallpaperItemStore _itemStore;
    private readonly IItemLauncher _itemLauncher;
    private readonly ILoggingService _logger;

    public CurrentItemLaunchCoordinator(
        ICurrentWallpaperItemStore itemStore,
        IItemLauncher itemLauncher,
        ILoggingService logger,
        ISlideshowService? slideshowService = null)
    {
        _itemStore = itemStore;
        _itemLauncher = itemLauncher;
        _logger = logger;
        _slideshowService = slideshowService;
    }

    public MediaItem? ResolveCurrentItem(MediaItem? preferredFallbackItem = null)
    {
        var liveItem = _slideshowService?.CurrentState.CurrentItem;
        if (IsValidItem(liveItem))
        {
            return liveItem;
        }

        if (IsValidItem(preferredFallbackItem))
        {
            return preferredFallbackItem;
        }

        var persistedItem = _itemStore.Load();
        return IsValidItem(persistedItem) ? persistedItem : null;
    }

    public void RecordDisplayedItem(MediaItem? item)
    {
        if (!IsValidItem(item))
        {
            return;
        }

        _itemStore.Save(item!);
    }

    public async Task<ItemLaunchResult> LaunchCurrentItemAsync(
        MediaItem? preferredFallbackItem = null,
        CancellationToken cancellationToken = default)
    {
        var item = ResolveCurrentItem(preferredFallbackItem);
        if (item is null)
        {
            _logger.Warning("درخواست باز کردن عنوان فعلی به دلیل نبودن اطلاعات عنوان معتبر متوقف شد.");
            return new ItemLaunchResult(false, MissingItemErrorMessage);
        }

        return await _itemLauncher.OpenItemAsync(item, cancellationToken);
    }

    private static bool IsValidItem(MediaItem? item)
        => item is not null &&
           !string.IsNullOrWhiteSpace(item.Id) &&
           !string.IsNullOrWhiteSpace(item.Name);
}
