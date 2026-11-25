namespace JellyfinDesktopSlideshow.Services;

public interface IExternalLauncher
{
    bool OpenHttpUrl(Uri uri);
    bool IsJellyfinDesktopInstalled();
}
