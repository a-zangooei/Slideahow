using System.Windows.Threading;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class WpfDispatcherTimerAdapter : ISlideshowTimer
{
    private readonly DispatcherTimer _timer;

    public WpfDispatcherTimerAdapter()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += (_, e) => Tick?.Invoke(this, e);
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsEnabled => _timer.IsEnabled;

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
