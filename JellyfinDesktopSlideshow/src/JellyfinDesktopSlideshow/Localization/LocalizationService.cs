using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace JellyfinDesktopSlideshow.Localization;

public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultLanguageCode = "fa-IR";
    public const string EnglishFallbackLanguageCode = "en-US";

    private readonly Lock _syncLock = new();
    private readonly Dictionary<string, LanguagePack> _packsByCode;
    private readonly IReadOnlyList<LanguageDefinition> _availableLanguages;
    private LanguagePack _currentPack;
    private CultureInfo _currentCulture;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public LocalizationService(ILanguagePackProvider? provider = null, string? initialLanguageCode = null)
    {
        var packs = (provider ?? new JsonLanguagePackProvider()).LoadLanguagePacks();
        _packsByCode = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in packs)
        {
            _packsByCode[pack.Definition.Code] = pack;
        }

        if (_packsByCode.Count == 0)
        {
            var emptyPack = new LanguagePack
            {
                Definition = new LanguageDefinition(DefaultLanguageCode, "فارسی", true),
                Strings = new Dictionary<string, string>(StringComparer.Ordinal)
            };
            _packsByCode[emptyPack.Definition.Code] = emptyPack;
        }

        _availableLanguages = _packsByCode.Values
            .Select(p => p.Definition)
            .ToList();

        var resolvedCode = ResolvePackCode(initialLanguageCode);
        _currentPack = _packsByCode[resolvedCode];
        _currentCulture = ResolveCulture(_currentPack.Definition.Code);
    }

    public LanguageDefinition CurrentLanguage
    {
        get
        {
            lock (_syncLock)
            {
                return _currentPack.Definition;
            }
        }
    }

    public IReadOnlyList<LanguageDefinition> AvailableLanguages => _availableLanguages;

    public bool IsRightToLeft => CurrentLanguage.IsRightToLeft;

    public FlowDirection CurrentFlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public CultureInfo CurrentCulture
    {
        get
        {
            lock (_syncLock)
            {
                return _currentCulture;
            }
        }
    }

    public string this[string key] => GetString(key);

    public bool SetLanguage(string? languageCode)
    {
        var resolvedCode = ResolvePackCode(languageCode);
        var changed = false;

        lock (_syncLock)
        {
            if (!string.Equals(_currentPack.Definition.Code, resolvedCode, StringComparison.OrdinalIgnoreCase))
            {
                _currentPack = _packsByCode[resolvedCode];
                _currentCulture = ResolveCulture(_currentPack.Definition.Code);
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightToLeft)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentFlowDirection)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public string NormalizeLanguageCode(string? languageCode) => ResolvePackCode(languageCode);

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        lock (_syncLock)
        {
            if (_currentPack.Strings.TryGetValue(key, out var value))
            {
                return value;
            }

            if (_packsByCode.TryGetValue(EnglishFallbackLanguageCode, out var enPack) &&
                enPack.Strings.TryGetValue(key, out var enValue))
            {
                return enValue;
            }

            if (_packsByCode.TryGetValue(DefaultLanguageCode, out var faPack) &&
                faPack.Strings.TryGetValue(key, out var faValue))
            {
                return faValue;
            }
        }

        return $"[{key}]";
    }

    public string Format(string key, params object?[] args)
    {
        var template = GetString(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private string ResolvePackCode(string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var trimmed = candidate.Trim();
            if (_packsByCode.TryGetValue(trimmed, out var exact))
            {
                return exact.Definition.Code;
            }

            var prefix = trimmed.Split('-', '_')[0];
            var byPrefix = _packsByCode.Values.FirstOrDefault(p =>
                string.Equals(p.Definition.Code.Split('-')[0], prefix, StringComparison.OrdinalIgnoreCase));
            if (byPrefix is not null)
            {
                return byPrefix.Definition.Code;
            }
        }

        if (_packsByCode.TryGetValue(DefaultLanguageCode, out var defaultPack))
        {
            return defaultPack.Definition.Code;
        }

        if (_packsByCode.TryGetValue(EnglishFallbackLanguageCode, out var englishPack))
        {
            return englishPack.Definition.Code;
        }

        return _packsByCode.Keys.First();
    }

    private static CultureInfo ResolveCulture(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
