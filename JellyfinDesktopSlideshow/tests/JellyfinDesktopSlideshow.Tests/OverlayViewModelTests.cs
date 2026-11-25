using System.IO;
using JellyfinDesktopSlideshow.Models;
using JellyfinDesktopSlideshow.Services;
using JellyfinDesktopSlideshow.ViewModels;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class OverlayViewModelTests
{
    [Fact]
    public async Task OverlayViewModel_SyncsWithSlideshowStateAndLaunchesCurrentItem()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "JellyfinSlideshowOverlayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var settingsService = new SettingsService(new NullLoggingService(), Path.Combine(tempDir, "settings.json"));
            settingsService.Current.OverlaySize = 60;
            settingsService.Current.OverlayOpacity = 0.75;

            var slideshow = new FakeSlideshowService();
            var launcher = new FakeItemLauncher();
            var vm = new OverlayViewModel(slideshow, launcher, settingsService, new NullLoggingService());

            Assert.Equal(60, vm.OverlaySize);
            Assert.Equal(0.75, vm.OverlayOpacity);
            Assert.False(vm.OpenCurrentItemCommand.CanExecute(null));

            var item = new MediaItem
            {
                Id = "series-99",
                Name = "Andor",
                Type = "Series",
                ProductionYear = 2022
            };

            slideshow.RaiseState(new SlideshowState
            {
                IsRunning = true,
                CurrentItem = item
            });

            Assert.True(vm.HasCurrentItem);
            Assert.Contains("Andor (2022)", vm.ToolTipText);
            Assert.True(vm.OpenCurrentItemCommand.CanExecute(null));

            var launchCompletion = new TaskCompletionSource<ItemLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.LaunchCompleted += (_, result) => launchCompletion.TrySetResult(result);

            vm.OpenCurrentItemCommand.Execute(null);
            var capturedResult = await launchCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(launcher.LaunchedItems);
            Assert.Equal("series-99", launcher.LaunchedItems[0].Id);
            Assert.NotNull(capturedResult);
            Assert.True(capturedResult.Succeeded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class FakeSlideshowService : ISlideshowService
    {
        public SlideshowState CurrentState { get; private set; } = new();

        public event EventHandler<SlideshowState>? StateChanged;

        public void RaiseState(SlideshowState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, state);
        }

        public void SetItems(IReadOnlyList<MediaItem> items) { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Pause() { }
        public void Resume() { }
        public void TogglePause() { }
        public void Stop() { }
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdateConfiguration() { }
    }

    private sealed class FakeItemLauncher : IItemLauncher
    {
        public List<MediaItem> LaunchedItems { get; } = [];

        public Task<ItemLaunchResult> OpenItemAsync(MediaItem item, CancellationToken cancellationToken = default)
        {
            LaunchedItems.Add(item);
            return Task.FromResult(new ItemLaunchResult(true, "Opened"));
        }

        public bool OpenValidatedBrowserUrl(string url) => true;
        public bool TryValidateCustomUrlTemplate(string? template, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public Uri BuildWebDetailsUri(Uri serverUri, string itemId, string? serverId)
            => new($"{serverUri}web/index.html#!/details?id={itemId}");
    }
}
