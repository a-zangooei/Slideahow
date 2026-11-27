using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public interface ISettingsService
{
    string SettingsFilePath { get; }
    AppSettings Current { get; }
    AppSettings Load();
    void Save(AppSettings settings);
    void ResetInMemoryToDefaults();
    bool TryValidateServerUrl(string rawUrl, out Uri? normalizedUri, out string? errorMessage);
    bool IsNonLocalPlainHttp(Uri serverUri);
}
