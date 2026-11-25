using JellyfinDesktopSlideshow.Infrastructure;
using Xunit;

namespace JellyfinDesktopSlideshow.Tests;

public sealed class StartupServiceTests
{
    [Fact]
    public void SetStartupEnabled_TrueWithoutMinimized_WritesQuotedExecutablePath()
    {
        var registry = new InMemoryStartupRegistryAdapter();
        var service = new StartupService(
            registry,
            new NullLoggingService(),
            () => @"C:\Program Files\JellyfinSlideshow\JellyfinDesktopSlideshow.exe");

        var ok = service.SetStartupEnabled(enabled: true, startMinimized: false);

        Assert.True(ok);
        Assert.True(service.IsStartupEnabled());
        Assert.Equal(
            "\"C:\\Program Files\\JellyfinSlideshow\\JellyfinDesktopSlideshow.exe\"",
            registry.Value);
    }

    [Fact]
    public void SetStartupEnabled_TrueWithMinimized_AppendsMinimizedArgument()
    {
        var registry = new InMemoryStartupRegistryAdapter();
        var service = new StartupService(
            registry,
            new NullLoggingService(),
            () => @"C:\Apps\JellyfinDesktopSlideshow.exe");

        var ok = service.SetStartupEnabled(enabled: true, startMinimized: true);

        Assert.True(ok);
        Assert.Equal(
            "\"C:\\Apps\\JellyfinDesktopSlideshow.exe\" --minimized",
            registry.Value);
    }

    [Fact]
    public void SetStartupEnabled_False_RemovesRegistryEntry()
    {
        var registry = new InMemoryStartupRegistryAdapter();
        var service = new StartupService(
            registry,
            new NullLoggingService(),
            () => @"C:\Apps\JellyfinDesktopSlideshow.exe");

        service.SetStartupEnabled(enabled: true, startMinimized: false);
        Assert.True(service.IsStartupEnabled());

        var disabled = service.SetStartupEnabled(enabled: false, startMinimized: false);

        Assert.True(disabled);
        Assert.False(service.IsStartupEnabled());
        Assert.Null(registry.Value);
    }

    private sealed class InMemoryStartupRegistryAdapter : IStartupRegistryAdapter
    {
        public string? Value { get; private set; }

        public string? ReadRunValue(string valueName) => Value;

        public void WriteRunValue(string valueName, string command) => Value = command;

        public void DeleteRunValue(string valueName) => Value = null;
    }
}
