namespace JellyfinDesktopSlideshow.Infrastructure;

public interface ITrayService : IDisposable
{
    event EventHandler? ShowMainWindowRequested;
    event EventHandler? OpenSettingsRequested;
    event EventHandler? TogglePauseRequested;
    event EventHandler? NextSlideRequested;
    event EventHandler? OpenCurrentItemRequested;
    event EventHandler? CreateDesktopShortcutRequested;
    event EventHandler? ExitApplicationRequested;

    void Initialize();
    void UpdateState(string tooltipText, bool isPaused, bool canTogglePause, bool canOpenCurrentItem);
    void UpdateLocalization();
}
