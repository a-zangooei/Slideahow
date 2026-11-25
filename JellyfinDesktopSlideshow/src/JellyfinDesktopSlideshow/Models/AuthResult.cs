namespace JellyfinDesktopSlideshow.Models;

public sealed class AuthResult
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string AccessToken { get; init; }
    public required string ServerId { get; init; }
}
