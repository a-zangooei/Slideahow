namespace JellyfinDesktopSlideshow.Models;

public enum SlideshowOrderMode
{
    Random = 0,
    Sequential = 1
}

public enum OfflineBehaviorMode
{
    UseCacheAndRetry = 0,
    KeepCurrentWallpaper = 1,
    PauseSlideshow = 2
}

public sealed class AppSettings
{
    public const string ApiKeyAuthMethod = "ApiKey";
    public const string PasswordAuthMethod = "AuthenticateByName";

    public string ServerUrl { get; set; } = string.Empty;
    public string? ServerId { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string AuthMethod { get; set; } = ApiKeyAuthMethod;
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string TokenCredentialKey { get; set; } = "JellyfinDesktopSlideshow:AccessToken";

    public List<string> SelectedLibraryIds { get; set; } = [];
    public List<string> IncludedItemTypes { get; set; } = ["Movie", "Series"];

    public int SlideshowIntervalSeconds { get; set; } = 300;
    public SlideshowOrderMode OrderMode { get; set; } = SlideshowOrderMode.Random;
    public int NonRepeatHistoryCount { get; set; } = 10;
    public OfflineBehaviorMode OfflineBehavior { get; set; } = OfflineBehaviorMode.UseCacheAndRetry;

    public bool ApplyToDesktopWallpaper { get; set; } = true;
    public WallpaperFitMode WallpaperFitMode { get; set; } = WallpaperFitMode.Fill;
    public bool RestoreWallpaperOnExit { get; set; } = false;

    public int MaxCacheSizeMb { get; set; } = 512;
    public int MaxCacheAgeDays { get; set; } = 14;
    public int BackdropIndexPreference { get; set; } = 0;
    public bool RandomBackdropPerItem { get; set; } = false;
    public bool FilterNonWallpaperAspectRatios { get; set; } = true;
    public BackdropAspectFilterPreset BackdropAspectFilterPreset { get; set; } = BackdropAspectFilterPreset.Standard;
    public double MinBackdropAspectRatio { get; set; } = 1.25;
    public double MaxBackdropAspectRatio { get; set; } = 2.80;
    public string? FallbackImagePath { get; set; }

    public bool ShowOverlayIcon { get; set; } = true;
    public bool ShowDesktopShortcutIcon { get; set; } = false;
    public double OverlayLeft { get; set; } = 32;
    public double OverlayTop { get; set; } = 32;
    public double OverlaySize { get; set; } = 44;
    public double OverlayOpacity { get; set; } = 0.85;

    public PlaybackTargetSettings PlaybackTarget { get; set; } = new();
    public string Language { get; set; } = "fa-IR";
    public bool LaunchOnWindowsStartup { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
}

public enum WallpaperFitMode
{
    Fill = 0,
    Fit = 1,
    Stretch = 2,
    Center = 3,
    Span = 4
}

public enum DesktopIconMode
{
    FloatingOverlay = 0,
    DesktopShortcut = 1,
    Both = 2,
    Disabled = 3
}

public enum BackdropAspectFilterPreset
{
    Standard = 0,
    StrictWidescreen = 1,
    RelaxedBannerOnly = 2,
    Custom = 3
}

public enum JellyfinAuthMethod
{
    ApiKey = 0,
    UsernamePassword = 1
}
