namespace JellyfinDesktopSlideshow.Models;

public sealed class JellyfinSessionInfo
{
    public required string Id { get; init; }
    public string? Client { get; init; }
    public string? DeviceName { get; init; }
    public string? DeviceId { get; init; }
    public string? UserId { get; init; }
    public bool SupportsRemoteControl { get; init; }
}
