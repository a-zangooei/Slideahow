namespace JellyfinDesktopSlideshow.Localization;

public sealed class LanguagePack
{
    public required LanguageDefinition Definition { get; init; }
    public required IReadOnlyDictionary<string, string> Strings { get; init; }
}
