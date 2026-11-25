namespace JellyfinDesktopSlideshow.Infrastructure;

public interface IStartupService
{
    bool IsStartupEnabled();
    bool SetStartupEnabled(bool enabled, bool startMinimized);
}

public interface IStartupRegistryAdapter
{
    string? ReadRunValue(string valueName);
    void WriteRunValue(string valueName, string command);
    void DeleteRunValue(string valueName);
}
