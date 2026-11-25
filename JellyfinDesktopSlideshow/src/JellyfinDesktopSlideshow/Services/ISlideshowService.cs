using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class SlideshowState
{
    public MediaItem? CurrentItem { get; init; }
    public string? CurrentItemId => CurrentItem?.Id;
    public string? CurrentImagePath { get; init; }
    public bool IsRunning { get; init; }
    public bool IsPaused { get; init; }
    public bool CanGoPrevious { get; init; }
    public int TotalItemsCount { get; init; }
    public int ConsecutiveFailureCount { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public interface ISlideshowService
{
    SlideshowState CurrentState { get; }
    event EventHandler<SlideshowState>? StateChanged;

    void SetItems(IReadOnlyList<MediaItem> items);
    Task StartAsync(CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    void TogglePause();
    Task NextAsync(CancellationToken cancellationToken = default);
    Task PreviousAsync(CancellationToken cancellationToken = default);
    void UpdateConfiguration();
    void Stop();
}
