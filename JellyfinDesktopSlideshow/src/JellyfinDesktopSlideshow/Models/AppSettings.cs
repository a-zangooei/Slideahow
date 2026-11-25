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
    public string ServerUrl { get; set; } = string.Empty;
    public string? ServerId { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string AuthMethod { get; set; } = "AuthenticateByName";
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
    public string? FallbackImagePath { get; set; }

    public bool ShowOverlayIcon { get; set; } = true;
    public double OverlayLeft { get; set; } = 32;
    public double OverlayTop { get; set; } = 32;
    public double OverlaySize { get; set; } = 44;
    public double OverlayOpacity { get; set; } = 0.85;

    public PlaybackTargetSettings PlaybackTarget { get; set; } = new();
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
