namespace JellyfinDesktopSlideshow.Models;

public sealed class JellyfinLibrary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? CollectionType { get; init; }
}
