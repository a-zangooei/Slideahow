using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    AppSettings Load();
    void Save(AppSettings settings);
    bool TryValidateServerUrl(string rawUrl, out Uri? normalizedUri, out string? errorMessage);
    bool IsNonLocalPlainHttp(Uri serverUri);
}
