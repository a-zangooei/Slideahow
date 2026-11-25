namespace JellyfinDesktopSlideshow.Models;

public enum PlaybackTargetType
{
    DefaultBrowser = 0,
    JellyfinDesktopClient = 1,
    CustomUrlTemplate = 2
}

public sealed class PlaybackTargetSettings
{
    public PlaybackTargetType TargetType { get; set; } = PlaybackTargetType.DefaultBrowser;
    public string? CustomUrlTemplate { get; set; }
}
