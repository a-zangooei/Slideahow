using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface ICurrentWallpaperItemStore
{
    MediaItem? Load();
    void Save(MediaItem item);
    void Clear();
}
