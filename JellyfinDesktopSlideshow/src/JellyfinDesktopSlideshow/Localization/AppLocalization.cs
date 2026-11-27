using System.Windows;

namespace JellyfinDesktopSlideshow.Localization;

public static class AppLocalization
{
    private static ILocalizationService _current = new LocalizationService();

    public static ILocalizationService Current => _current;

    public static void Configure(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        _current = localizationService;
    }

    public static MessageBoxOptions GetMessageBoxOptions()
        => Current.IsRightToLeft
            ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
            : MessageBoxOptions.None;
}
