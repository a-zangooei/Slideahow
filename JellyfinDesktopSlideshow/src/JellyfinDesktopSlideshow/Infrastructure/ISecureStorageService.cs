namespace JellyfinDesktopSlideshow.Infrastructure;

public interface ISecureStorageService
{
    void SaveSecret(string key, string username, string secret);
    string? ReadSecret(string key);
    void DeleteSecret(string key);
}
