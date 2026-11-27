namespace JellyfinDesktopSlideshow.Localization;

public interface ILanguagePackProvider
{
    IReadOnlyList<LanguagePack> LoadLanguagePacks();
}
