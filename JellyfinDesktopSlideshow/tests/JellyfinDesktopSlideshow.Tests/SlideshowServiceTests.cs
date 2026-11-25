using System.IO;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class SlideshowServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"jf-slide-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task StartNextPrevious_SequentialOrder_TracksHistoryAndItemId()
    {
        var (service, timer, settings, _) = CreateSlideshow();
        settings.Current.OrderMode = SlideshowOrderMode.Sequential;
        settings.Current.SlideshowIntervalSeconds = 120;

        service.SetItems(
        [
            new MediaItem { Id = "id-1", Name = "First", BackdropImageTags = ["t1"] },
            new MediaItem { Id = "id-2", Name = "Second", BackdropImageTags = ["t2"] },
            new MediaItem { Id = "id-3", Name = "Third", BackdropImageTags = ["t3"] }
        ]);

        await service.StartAsync();
        Assert.True(timer.IsEnabled);
        Assert.Equal("id-1", service.CurrentState.CurrentItemId);
        Assert.False(service.CurrentState.CanGoPrevious);

        await service.NextAsync();
        Assert.Equal("id-2", service.CurrentState.CurrentItemId);
        Assert.True(service.CurrentState.CanGoPrevious);

        await service.NextAsync();
        Assert.Equal("id-3", service.CurrentState.CurrentItemId);

        await service.PreviousAsync();
        Assert.Equal("id-2", service.CurrentState.CurrentItemId);

        await service.NextAsync();
        Assert.Equal("id-3", service.CurrentState.CurrentItemId);
    }

    [Fact]
    public async Task RandomOrder_RespectsNonRepeatHistoryWindow()
    {
        var (service, _, settings, _) = CreateSlideshow();
        settings.Current.OrderMode = SlideshowOrderMode.Random;
        settings.Current.NonRepeatHistoryCount = 3;

        service.SetItems(
        [
            new MediaItem { Id = "a", Name = "A", BackdropImageTags = ["t"] },
            new MediaItem { Id = "b", Name = "B", BackdropImageTags = ["t"] },
            new MediaItem { Id = "c", Name = "C", BackdropImageTags = ["t"] },
            new MediaItem { Id = "d", Name = "D", BackdropImageTags = ["t"] }
        ]);

        await service.StartAsync();
        var seen = new List<string> { service.CurrentState.CurrentItemId! };

        for (var i = 0; i < 8; i++)
        {
            await service.NextAsync();
            var current = service.CurrentState.CurrentItemId!;
            Assert.DoesNotContain(current, seen.TakeLast(3));
            seen.Add(current);
        }
    }

    [Fact]
    public async Task Advance_SkipsItemsWithoutBackdrop()
    {
        var (service, _, settings, imageService) = CreateSlideshow();
        settings.Current.OrderMode = SlideshowOrderMode.Sequential;
        imageService.ForceSkipIds.Add("no-bg");

        service.SetItems(
        [
            new MediaItem { Id = "no-bg", Name = "Missing", BackdropImageTags = [] },
            new MediaItem { Id = "has-bg", Name = "Valid", BackdropImageTags = ["t1"] }
        ]);

        await service.StartAsync();
        Assert.Equal("has-bg", service.CurrentState.CurrentItemId);
    }

    [Fact]
    public async Task PauseAndResume_ControlTimerState()
    {
        var (service, timer, _, _) = CreateSlideshow();
        service.SetItems([new MediaItem { Id = "1", Name = "One", BackdropImageTags = ["t"] }]);

        await service.StartAsync();
        Assert.True(timer.IsEnabled);

        service.Pause();
        Assert.False(timer.IsEnabled);
        Assert.True(service.CurrentState.IsPaused);

        service.Resume();
        Assert.True(timer.IsEnabled);
        Assert.False(service.CurrentState.IsPaused);
    }

    [Fact]
    public async Task OfflineFailure_WithPauseMode_PreservesCurrentItemAndPauses()
    {
        var (service, timer, settings, imageService) = CreateSlideshow();
        settings.Current.OrderMode = SlideshowOrderMode.Sequential;
        settings.Current.OfflineBehavior = OfflineBehaviorMode.PauseSlideshow;

        service.SetItems(
        [
            new MediaItem { Id = "1", Name = "One", BackdropImageTags = ["t"] },
            new MediaItem { Id = "2", Name = "Two", BackdropImageTags = ["t"] }
        ]);

        await service.StartAsync();
        var firstPath = service.CurrentState.CurrentImagePath;
        Assert.Equal("1", service.CurrentState.CurrentItemId);

        imageService.ForceFailIds.Add("2");
        await service.NextAsync();

        Assert.False(timer.IsEnabled);
        Assert.True(service.CurrentState.IsPaused);
        Assert.Equal("1", service.CurrentState.CurrentItemId);
        Assert.Equal(firstPath, service.CurrentState.CurrentImagePath);
    }

    private (SlideshowService Service, FakeTimer Timer, SettingsService Settings, FakeImageService ImageService) CreateSlideshow()
    {
        Directory.CreateDirectory(_tempDir);
        var logger = new NullLoggingService();
        var settings = new SettingsService(logger, Path.Combine(_tempDir, "settings.json"));
        settings.Load();

        var timer = new FakeTimer();
        var imageService = new FakeImageService(_tempDir);
        var service = new SlideshowService(imageService, settings, timer, logger);
        return (service, timer, settings, imageService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class FakeTimer : ISlideshowTimer
    {
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;

        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeImageService : IImageService
    {
        private readonly string _dir;
        public HashSet<string> ForceSkipIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ForceFailIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public FakeImageService(string dir) => _dir = dir;

        public Task<BackdropResolveResult> ResolveBackdropAsync(MediaItem item, CancellationToken cancellationToken = default)
        {
            if (ForceSkipIds.Contains(item.Id))
            {
                return Task.FromResult(new BackdropResolveResult
                {
                    Status = BackdropResolveStatus.SkippedNoBackdrop,
                    Message = "بدون Backdrop"
                });
            }

            if (ForceFailIds.Contains(item.Id))
            {
                return Task.FromResult(new BackdropResolveResult
                {
                    Status = BackdropResolveStatus.FailedCorruptOrIo,
                    Message = "خطای شبکه"
                });
            }

            var path = Path.Combine(_dir, $"{item.Id}.png");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, CacheServiceTests.CreateValidPngBytes(128));
            }

            return Task.FromResult(new BackdropResolveResult
            {
                Status = BackdropResolveStatus.Success,
                LocalFilePath = path,
                Message = "OK"
            });
        }
    }
}
