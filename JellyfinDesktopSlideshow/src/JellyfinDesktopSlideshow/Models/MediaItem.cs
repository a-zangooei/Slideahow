namespace JellyfinDesktopSlideshow.Models;

public sealed class MediaItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Type { get; init; }
    public int? ProductionYear { get; init; }
    public string? BackdropImageUrl { get; init; }
    public IReadOnlyList<string> BackdropImageTags { get; init; } = [];

    public bool HasBackdrop => BackdropImageTags.Count > 0;
}
