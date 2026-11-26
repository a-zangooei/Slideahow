using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public static class ImageAspectFilter
{
    public static (double MinRatio, double MaxRatio) GetBoundsForPreset(BackdropAspectFilterPreset preset)
        => preset switch
        {
            BackdropAspectFilterPreset.StrictWidescreen => (1.45, 2.45),
            BackdropAspectFilterPreset.RelaxedBannerOnly => (1.05, 3.20),
            _ => (1.25, 2.80)
        };

    public static (double MinRatio, double MaxRatio) GetEffectiveBounds(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.BackdropAspectFilterPreset != BackdropAspectFilterPreset.Custom)
        {
            return GetBoundsForPreset(settings.BackdropAspectFilterPreset);
        }

        var min = Math.Clamp(settings.MinBackdropAspectRatio, 0.2, 3.0);
        var max = Math.Clamp(settings.MaxBackdropAspectRatio, Math.Max(1.1, min + 0.1), 10.0);
        return (min, max);
    }

    public static bool IsAcceptableBytes(ReadOnlySpan<byte> imageBytes, AppSettings settings, out string? rejectionReason)
    {
        rejectionReason = null;
        if (!settings.FilterNonWallpaperAspectRatios)
        {
            return true;
        }

        if (!ImageFormatValidator.TryReadImageDimensions(imageBytes, out var width, out var height))
        {
            // Fail-open if header dimensions are unavailable so valid non-standard images are not falsely blocked.
            return true;
        }

        return IsAcceptableDimensions(width, height, settings, out rejectionReason);
    }

    public static bool IsAcceptableFile(string? filePath, AppSettings settings, out string? rejectionReason)
    {
        rejectionReason = null;
        if (!settings.FilterNonWallpaperAspectRatios)
        {
            return true;
        }

        if (!ImageFormatValidator.TryReadImageDimensions(filePath, out var width, out var height))
        {
            return true;
        }

        return IsAcceptableDimensions(width, height, settings, out rejectionReason);
    }

    public static bool IsAcceptableDimensions(int width, int height, AppSettings settings, out string? rejectionReason)
    {
        rejectionReason = null;
        if (!settings.FilterNonWallpaperAspectRatios || width <= 0 || height <= 0)
        {
            return true;
        }

        var (minRatio, maxRatio) = GetEffectiveBounds(settings);
        var aspectRatio = (double)width / height;

        if (aspectRatio < minRatio)
        {
            rejectionReason = $"تصویر عمودی یا مربعی است ({width}×{height}، نسبت {aspectRatio:F2} کمتر از {minRatio:F2}).";
            return false;
        }

        if (aspectRatio > maxRatio)
        {
            rejectionReason = $"بنر خیلی دراز است ({width}×{height}، نسبت {aspectRatio:F2} بیشتر از {maxRatio:F2}).";
            return false;
        }

        return true;
    }
}
