namespace JellyfinDesktopSlideshow.Services;

public interface IOverlayService
{
    bool IsVisible { get; }
    void Show();
    void Hide();
    void RefreshFromSettings();
    void Close();
}
