using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace JellyfinDesktopSlideshow.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    LanguageDefinition CurrentLanguage { get; }
    IReadOnlyList<LanguageDefinition> AvailableLanguages { get; }
    bool IsRightToLeft { get; }
    FlowDirection CurrentFlowDirection { get; }
    CultureInfo CurrentCulture { get; }

    string this[string key] { get; }

    event EventHandler? LanguageChanged;

    bool SetLanguage(string? languageCode);
    string NormalizeLanguageCode(string? languageCode);
    string GetString(string key);
    string Format(string key, params object?[] args);
}
