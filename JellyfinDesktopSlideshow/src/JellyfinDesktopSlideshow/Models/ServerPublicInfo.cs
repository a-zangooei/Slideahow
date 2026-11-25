namespace JellyfinDesktopSlideshow.Models;

public sealed class ServerPublicInfo
{
    public required string ServerId { get; init; }
    public required string ServerName { get; init; }
    public required string Version { get; init; }
    public string? ProductName { get; init; }
    public bool? StartupWizardCompleted { get; init; }
    public bool IsNonLocalPlainHttp { get; init; }
}
