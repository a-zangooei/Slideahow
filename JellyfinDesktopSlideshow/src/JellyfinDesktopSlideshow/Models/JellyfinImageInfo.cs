namespace JellyfinDesktopSlideshow.Models;

public sealed class JellyfinImageInfo
{
    public required string ItemId { get; init; }
    public int ImageIndex { get; init; }
    public string? Tag { get; init; }
    public int MaxWidth { get; init; } = 1920;
    public int Quality { get; init; } = 92;
    public string? RequestUrl { get; init; }
}

public enum BackdropResolveStatus
{
    Success = 0,
    UsedCacheOffline = 1,
    UsedFallback = 2,
    SkippedNoBackdrop = 3,
    FailedCorruptOrIo = 4
}

public sealed class BackdropResolveResult
{
    public required BackdropResolveStatus Status { get; init; }
    public string? LocalFilePath { get; init; }
    public JellyfinImageInfo? ImageInfo { get; init; }
    public string Message { get; init; } = string.Empty;

    public bool HasImage =>
        !string.IsNullOrWhiteSpace(LocalFilePath) &&
        Status is BackdropResolveStatus.Success
            or BackdropResolveStatus.UsedCacheOffline
            or BackdropResolveStatus.UsedFallback;
}

public sealed class CacheStats
{
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
}
