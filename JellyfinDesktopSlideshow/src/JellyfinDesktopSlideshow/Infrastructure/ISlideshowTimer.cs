namespace JellyfinDesktopSlideshow.Infrastructure;

public interface ISlideshowTimer
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    event EventHandler? Tick;
    void Start();
    void Stop();
}
