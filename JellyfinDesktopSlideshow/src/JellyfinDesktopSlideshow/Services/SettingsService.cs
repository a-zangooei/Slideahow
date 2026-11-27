using System.IO;
using System.Net;
using System.Text.Json;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;

    public string SettingsFilePath { get; }
    public AppSettings Current { get; private set; } = new();

    public SettingsService(
        ILoggingService logger,
        string? settingsFilePath = null,
        ILocalizationService? localization = null)
    {
        _logger = logger;
        _localization = localization ?? AppLocalization.Current;
        SettingsFilePath = settingsFilePath ?? AppDataPaths.Default.SettingsFilePath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Current = new AppSettings
                {
                    Language = _localization.NormalizeLanguageCode(null)
                };
                Save(Current);
                return Current;
            }

            var json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            Current = loaded ?? new AppSettings();

            if (string.IsNullOrWhiteSpace(Current.DeviceId))
            {
                Current.DeviceId = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(Current.TokenCredentialKey))
            {
                Current.TokenCredentialKey = "JellyfinDesktopSlideshow:AccessToken";
            }

            if (!string.Equals(Current.AuthMethod, AppSettings.ApiKeyAuthMethod, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Current.AuthMethod, AppSettings.PasswordAuthMethod, StringComparison.OrdinalIgnoreCase))
            {
                Current.AuthMethod = string.IsNullOrWhiteSpace(Current.UserId)
                    ? AppSettings.ApiKeyAuthMethod
                    : AppSettings.PasswordAuthMethod;
            }
            else if (string.Equals(Current.AuthMethod, AppSettings.ApiKeyAuthMethod, StringComparison.OrdinalIgnoreCase))
            {
                Current.AuthMethod = AppSettings.ApiKeyAuthMethod;
            }
            else
            {
                Current.AuthMethod = AppSettings.PasswordAuthMethod;
            }

            Current.SlideshowIntervalSeconds = Math.Clamp(Current.SlideshowIntervalSeconds, 5, 86400);
            Current.NonRepeatHistoryCount = Math.Clamp(Current.NonRepeatHistoryCount, 0, 100);
            Current.MaxCacheSizeMb = Math.Clamp(Current.MaxCacheSizeMb, 32, 8192);
            Current.MaxCacheAgeDays = Math.Clamp(Current.MaxCacheAgeDays, 1, 365);
            Current.BackdropIndexPreference = Math.Clamp(Current.BackdropIndexPreference, 0, 20);
            Current.MinBackdropAspectRatio = Math.Clamp(Current.MinBackdropAspectRatio, 0.2, 3.0);
            Current.MaxBackdropAspectRatio = Math.Clamp(
                Current.MaxBackdropAspectRatio,
                Math.Max(1.1, Current.MinBackdropAspectRatio + 0.1),
                10.0);
            Current.OverlaySize = Math.Clamp(Current.OverlaySize, 24, 96);
            Current.OverlayOpacity = Math.Clamp(Current.OverlayOpacity, 0.2, 1.0);
            Current.PlaybackTarget ??= new PlaybackTargetSettings();
            Current.SelectedLibraryIds ??= [];
            Current.IncludedItemTypes ??= ["Movie", "Series"];
            Current.Language = _localization.NormalizeLanguageCode(Current.Language);

            return Current;
        }
        catch (Exception ex)
        {
            _logger.Error("خطا در خواندن فایل تنظیمات؛ تنظیمات پیش‌فرض بارگذاری شد.", ex);
            Current = new AppSettings
            {
                Language = _localization.NormalizeLanguageCode(null)
            };
            return Current;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = $"{SettingsFilePath}.tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsFilePath, overwrite: true);
        Current = settings;
    }

    public void ResetInMemoryToDefaults()
    {
        var activeLanguage = _localization.CurrentLanguage.Code;
        Current = new AppSettings
        {
            Language = activeLanguage
        };
    }

    public bool TryValidateServerUrl(string rawUrl, out Uri? normalizedUri, out string? errorMessage)
    {
        normalizedUri = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            errorMessage = _localization.GetString("Validation.ServerUrl.Empty");
            return false;
        }

        var trimmed = rawUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            errorMessage = _localization.GetString("Validation.ServerUrl.Format");
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage = _localization.GetString("Validation.ServerUrl.Scheme");
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.Host))
        {
            errorMessage = _localization.GetString("Validation.ServerUrl.Host");
            return false;
        }

        var builder = new UriBuilder(parsed)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        normalizedUri = builder.Uri;
        return true;
    }

    public bool IsNonLocalPlainHttp(Uri serverUri)
    {
        if (serverUri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (serverUri.IsLoopback || string.Equals(serverUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(serverUri.Host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
