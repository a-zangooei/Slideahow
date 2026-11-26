using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class SlideshowService : ISlideshowService
{
    private const int MaxHistoryEntries = 100;
    private const int MaxBackoffSeconds = 300;

    private readonly IImageService _imageService;
    private readonly ISettingsService _settingsService;
    private readonly ISlideshowTimer _timer;
    private readonly ILoggingService _logger;
    private readonly ICurrentWallpaperItemStore? _currentItemStore;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);

    private readonly List<MediaItem> _items = [];
    private readonly List<string> _history = [];
    private int _historyCursor = -1;
    private int _sequentialIndex = -1;

    public SlideshowState CurrentState { get; private set; } = new()
    {
        StatusMessage = "اسلایدشو آماده است."
    };

    public event EventHandler<SlideshowState>? StateChanged;

    public SlideshowService(
        IImageService imageService,
        ISettingsService settingsService,
        ISlideshowTimer timer,
        ILoggingService logger,
        ICurrentWallpaperItemStore? currentItemStore = null)
    {
        _imageService = imageService;
        _settingsService = settingsService;
        _timer = timer;
        _logger = logger;
        _currentItemStore = currentItemStore;

        _timer.Interval = GetConfiguredInterval();
        _timer.Tick += OnTimerTick;
    }

    public void SetItems(IReadOnlyList<MediaItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
        _sequentialIndex = -1;

        var currentId = CurrentState.CurrentItemId;
        if (currentId is not null)
        {
            var existingIdx = _items.FindIndex(i => string.Equals(i.Id, currentId, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0)
            {
                _sequentialIndex = existingIdx;
            }
        }

        PublishState(new SlideshowState
        {
            CurrentItem = CurrentState.CurrentItem,
            CurrentImagePath = CurrentState.CurrentImagePath,
            IsRunning = CurrentState.IsRunning,
            IsPaused = CurrentState.IsPaused,
            CanGoPrevious = _historyCursor > 0,
            TotalItemsCount = _items.Count,
            ConsecutiveFailureCount = CurrentState.ConsecutiveFailureCount,
            StatusMessage = _items.Count == 0
                ? "فهرست رسانه‌ها خالی است."
                : $"{_items.Count} عنوان در فهرست اسلایدشو آماده است."
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_items.Count == 0)
        {
            PublishState(new SlideshowState
            {
                IsRunning = false,
                IsPaused = false,
                TotalItemsCount = 0,
                StatusMessage = "عنوانی برای شروع اسلایدشو وجود ندارد."
            });
            return;
        }

        _timer.Interval = GetConfiguredInterval();

        if (!CurrentState.IsRunning || CurrentState.CurrentItem is null)
        {
            PublishState(new SlideshowState
            {
                CurrentItem = CurrentState.CurrentItem,
                CurrentImagePath = CurrentState.CurrentImagePath,
                IsRunning = true,
                IsPaused = false,
                CanGoPrevious = _historyCursor > 0,
                TotalItemsCount = _items.Count,
                StatusMessage = "در حال شروع اسلایدشو..."
            });

            await AdvanceInternalAsync(cancellationToken);
        }
        else
        {
            PublishState(new SlideshowState
            {
                CurrentItem = CurrentState.CurrentItem,
                CurrentImagePath = CurrentState.CurrentImagePath,
                IsRunning = true,
                IsPaused = false,
                CanGoPrevious = _historyCursor > 0,
                TotalItemsCount = _items.Count,
                ConsecutiveFailureCount = CurrentState.ConsecutiveFailureCount,
                StatusMessage = "اسلایدشو در حال اجرا است."
            });
        }

        if (CurrentState.IsRunning && !CurrentState.IsPaused)
        {
            _timer.Start();
        }
    }

    public void Pause()
    {
        if (!CurrentState.IsRunning || CurrentState.IsPaused)
        {
            return;
        }

        _timer.Stop();
        PublishState(new SlideshowState
        {
            CurrentItem = CurrentState.CurrentItem,
            CurrentImagePath = CurrentState.CurrentImagePath,
            IsRunning = true,
            IsPaused = true,
            CanGoPrevious = _historyCursor > 0,
            TotalItemsCount = _items.Count,
            ConsecutiveFailureCount = CurrentState.ConsecutiveFailureCount,
            StatusMessage = "اسلایدشو متوقف (مکث) شد."
        });
    }

    public void Resume()
    {
        if (!CurrentState.IsRunning || !CurrentState.IsPaused || _items.Count == 0)
        {
            return;
        }

        _timer.Interval = GetConfiguredInterval();
        _timer.Start();

        PublishState(new SlideshowState
        {
            CurrentItem = CurrentState.CurrentItem,
            CurrentImagePath = CurrentState.CurrentImagePath,
            IsRunning = true,
            IsPaused = false,
            CanGoPrevious = _historyCursor > 0,
            TotalItemsCount = _items.Count,
            ConsecutiveFailureCount = CurrentState.ConsecutiveFailureCount,
            StatusMessage = "اسلایدشو ادامه یافت."
        });
    }

    public void TogglePause()
    {
        if (CurrentState.IsPaused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    public Task NextAsync(CancellationToken cancellationToken = default)
        => AdvanceInternalAsync(cancellationToken);

    public async Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        if (!await _transitionGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_historyCursor <= 0 || _history.Count == 0)
            {
                return;
            }

            _historyCursor--;
            var targetId = _history[_historyCursor];
            var targetItem = _items.FirstOrDefault(i => string.Equals(i.Id, targetId, StringComparison.OrdinalIgnoreCase));
            if (targetItem is null)
            {
                return;
            }

            var result = await _imageService.ResolveBackdropAsync(targetItem, cancellationToken);
            if (result.HasImage)
            {
                ResetTimerIfActive();
                PublishState(new SlideshowState
                {
                    CurrentItem = targetItem,
                    CurrentImagePath = result.LocalFilePath,
                    IsRunning = CurrentState.IsRunning,
                    IsPaused = CurrentState.IsPaused,
                    CanGoPrevious = _historyCursor > 0,
                    TotalItemsCount = _items.Count,
                    ConsecutiveFailureCount = 0,
                    StatusMessage = $"عنوان قبلی: {FormatItemLabel(targetItem)}"
                });
            }
            else
            {
                PublishState(new SlideshowState
                {
                    CurrentItem = CurrentState.CurrentItem,
                    CurrentImagePath = CurrentState.CurrentImagePath,
                    IsRunning = CurrentState.IsRunning,
                    IsPaused = CurrentState.IsPaused,
                    CanGoPrevious = _historyCursor > 0,
                    TotalItemsCount = _items.Count,
                    ConsecutiveFailureCount = CurrentState.ConsecutiveFailureCount + 1,
                    StatusMessage = result.Message
                });
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public void UpdateConfiguration()
    {
        _timer.Interval = GetConfiguredInterval();
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _timer.Start();
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _history.Clear();
        _historyCursor = -1;
        _sequentialIndex = -1;

        PublishState(new SlideshowState
        {
            CurrentItem = null,
            CurrentImagePath = null,
            IsRunning = false,
            IsPaused = false,
            CanGoPrevious = false,
            TotalItemsCount = _items.Count,
            ConsecutiveFailureCount = 0,
            StatusMessage = "اسلایدشو متوقف شد."
        });
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await AdvanceInternalAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error("خطا در اجرای تیک اسلایدشو.", ex);
        }
    }

    private async Task AdvanceInternalAsync(CancellationToken cancellationToken)
    {
        if (_items.Count == 0)
        {
            return;
        }

        if (!await _transitionGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_historyCursor >= 0 && _historyCursor < _history.Count - 1)
            {
                _historyCursor++;
                var replayId = _history[_historyCursor];
                var replayItem = _items.FirstOrDefault(i => string.Equals(i.Id, replayId, StringComparison.OrdinalIgnoreCase));
                if (replayItem is not null)
                {
                    var replayResult = await _imageService.ResolveBackdropAsync(replayItem, cancellationToken);
                    if (replayResult.HasImage)
                    {
                        ResetTimerIfActive();
                        PublishState(new SlideshowState
                        {
                            CurrentItem = replayItem,
                            CurrentImagePath = replayResult.LocalFilePath,
                            IsRunning = CurrentState.IsRunning,
                            IsPaused = CurrentState.IsPaused,
                            CanGoPrevious = _historyCursor > 0,
                            TotalItemsCount = _items.Count,
                            ConsecutiveFailureCount = 0,
                            StatusMessage = $"{FormatItemLabel(replayItem)} — {replayResult.Message}"
                        });
                        return;
                    }
                }
            }

            var triedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxAttempts = _items.Count;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var candidate = SelectNextCandidate(triedIds);
                if (candidate is null)
                {
                    break;
                }

                triedIds.Add(candidate.Id);
                var result = await _imageService.ResolveBackdropAsync(candidate, cancellationToken);

                if (result.HasImage)
                {
                    AppendToHistory(candidate.Id);
                    ResetTimerIfActive();

                    PublishState(new SlideshowState
                    {
                        CurrentItem = candidate,
                        CurrentImagePath = result.LocalFilePath,
                        IsRunning = CurrentState.IsRunning,
                        IsPaused = CurrentState.IsPaused,
                        CanGoPrevious = _historyCursor > 0,
                        TotalItemsCount = _items.Count,
                        ConsecutiveFailureCount = 0,
                        StatusMessage = $"{FormatItemLabel(candidate)} — {result.Message}"
                    });
                    return;
                }

                if (result.Status == BackdropResolveStatus.SkippedNoBackdrop)
                {
                    continue;
                }

                // Network/IO/Corrupt failure: handle according to OfflineBehavior
                HandleFailure(result.Message);
                return;
            }

            PublishState(new SlideshowState
            {
                CurrentItem = CurrentState.CurrentItem,
                CurrentImagePath = CurrentState.CurrentImagePath,
                IsRunning = CurrentState.IsRunning,
                IsPaused = CurrentState.IsPaused,
                CanGoPrevious = _historyCursor > 0,
                TotalItemsCount = _items.Count,
                ConsecutiveFailureCount = CurrentState.ConsecutiveFailureCount,
                StatusMessage = "هیچ عنوانی در فهرست فعلی تصویر Backdrop یا جایگزین معتبر ندارد."
            });
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private MediaItem? SelectNextCandidate(HashSet<string> excludedInThisPass)
    {
        var settings = _settingsService.Current;
        if (settings.OrderMode == SlideshowOrderMode.Sequential)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                _sequentialIndex = (_sequentialIndex + 1) % _items.Count;
                var candidate = _items[_sequentialIndex];
                if (!excludedInThisPass.Contains(candidate.Id))
                {
                    return candidate;
                }
            }

            return null;
        }

        // Random mode with non-repeat window
        var nonRepeatLimit = Math.Clamp(settings.NonRepeatHistoryCount, 0, Math.Max(0, _items.Count - 1));
        var recentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (nonRepeatLimit > 0 && _history.Count > 0)
        {
            var start = Math.Max(0, _history.Count - nonRepeatLimit);
            for (var i = start; i < _history.Count; i++)
            {
                recentIds.Add(_history[i]);
            }
        }

        var preferred = _items
            .Where(item => !excludedInThisPass.Contains(item.Id) && !recentIds.Contains(item.Id))
            .ToList();

        if (preferred.Count > 0)
        {
            return preferred[Random.Shared.Next(preferred.Count)];
        }

        var fallback = _items
            .Where(item => !excludedInThisPass.Contains(item.Id))
            .ToList();

        return fallback.Count > 0 ? fallback[Random.Shared.Next(fallback.Count)] : null;
    }

    private void AppendToHistory(string itemId)
    {
        if (_historyCursor >= 0 && _historyCursor < _history.Count - 1)
        {
            _history.RemoveRange(_historyCursor + 1, _history.Count - _historyCursor - 1);
        }

        _history.Add(itemId);
        if (_history.Count > MaxHistoryEntries)
        {
            _history.RemoveAt(0);
        }

        _historyCursor = _history.Count - 1;
    }

    private void HandleFailure(string reason)
    {
        var failures = CurrentState.ConsecutiveFailureCount + 1;
        var mode = _settingsService.Current.OfflineBehavior;

        if (mode == OfflineBehaviorMode.PauseSlideshow)
        {
            _timer.Stop();
            PublishState(new SlideshowState
            {
                CurrentItem = CurrentState.CurrentItem,
                CurrentImagePath = CurrentState.CurrentImagePath,
                IsRunning = CurrentState.IsRunning,
                IsPaused = true,
                CanGoPrevious = _historyCursor > 0,
                TotalItemsCount = _items.Count,
                ConsecutiveFailureCount = failures,
                StatusMessage = $"اسلایدشو به دلیل خطای ارتباط متوقف شد: {reason}"
            });
            return;
        }

        // Spaced retry backoff when server is repeatedly unreachable
        if (_timer.IsEnabled && failures >= 3)
        {
            var baseSeconds = GetConfiguredInterval().TotalSeconds;
            var backoffSeconds = Math.Min(MaxBackoffSeconds, Math.Max(baseSeconds, 30 * Math.Pow(2, Math.Min(4, failures - 3))));
            _timer.Interval = TimeSpan.FromSeconds(backoffSeconds);
        }

        PublishState(new SlideshowState
        {
            CurrentItem = CurrentState.CurrentItem,
            CurrentImagePath = CurrentState.CurrentImagePath,
            IsRunning = CurrentState.IsRunning,
            IsPaused = CurrentState.IsPaused,
            CanGoPrevious = _historyCursor > 0,
            TotalItemsCount = _items.Count,
            ConsecutiveFailureCount = failures,
            StatusMessage = $"تصویر قبلی حفظ شد ({reason})."
        });
    }

    private void ResetTimerIfActive()
    {
        var configured = GetConfiguredInterval();
        if (_timer.Interval != configured)
        {
            _timer.Interval = configured;
        }
    }

    private TimeSpan GetConfiguredInterval()
    {
        var seconds = Math.Clamp(_settingsService.Current.SlideshowIntervalSeconds, 5, 86400);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string FormatItemLabel(MediaItem item)
        => item.ProductionYear.HasValue ? $"{item.Name} ({item.ProductionYear})" : item.Name;

    private void PublishState(SlideshowState state)
    {
        CurrentState = state;
        if (state.CurrentItem is not null && !string.IsNullOrWhiteSpace(state.CurrentImagePath))
        {
            _currentItemStore?.Save(state.CurrentItem);
        }

        StateChanged?.Invoke(this, state);
    }
}
