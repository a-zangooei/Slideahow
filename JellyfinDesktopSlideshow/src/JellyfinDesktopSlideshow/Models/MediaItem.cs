namespace JellyfinDesktopSlideshow.Models;

public sealed class MediaItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Type { get; init; }
    public int? ProductionYear { get; init; }
    public string? BackdropImageUrl { get; init; }
    public IReadOnlyList<string> BackdropImageTags { get; init; } = [];
    public IReadOnlyList<MediaItem> CollectionMembers { get; init; } = [];

    public bool IsCollection => string.Equals(Type, "BoxSet", StringComparison.OrdinalIgnoreCase);

    public bool HasBackdrop =>
        BackdropImageTags.Count > 0 ||
        (IsCollection && CollectionMembers.Any(member => member.HasBackdrop));
}
