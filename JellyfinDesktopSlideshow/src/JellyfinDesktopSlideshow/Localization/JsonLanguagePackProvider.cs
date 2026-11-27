using System.IO;
using System.Reflection;
using System.Text.Json;
using JellyfinDesktopSlideshow.Infrastructure;

namespace JellyfinDesktopSlideshow.Localization;

public sealed class JsonLanguagePackProvider : ILanguagePackProvider
{
    private const string EmbeddedResourcePrefix = "JellyfinDesktopSlideshow.Localization.Packs.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Assembly _resourceAssembly;
    private readonly string? _externalLanguagesDirectory;

    public JsonLanguagePackProvider(
        Assembly? resourceAssembly = null,
        string? externalLanguagesDirectory = null)
    {
        _resourceAssembly = resourceAssembly ?? typeof(JsonLanguagePackProvider).Assembly;
        _externalLanguagesDirectory = externalLanguagesDirectory ?? AppDataPaths.DefaultExternalLanguagesDirectory;
    }

    public IReadOnlyList<LanguagePack> LoadLanguagePacks()
    {
        var packsByCode = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);

        var resourceNames = _resourceAssembly
            .GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(EmbeddedResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in resourceNames)
        {
            using var stream = _resourceAssembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            var pack = TryDeserializeStream(stream);
            if (pack is not null)
            {
                packsByCode[pack.Definition.Code] = pack;
            }
        }

        if (!string.IsNullOrWhiteSpace(_externalLanguagesDirectory) && Directory.Exists(_externalLanguagesDirectory))
        {
            foreach (var filePath in Directory.GetFiles(_externalLanguagesDirectory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var stream = File.OpenRead(filePath);
                    var pack = TryDeserializeStream(stream);
                    if (pack is not null)
                    {
                        packsByCode[pack.Definition.Code] = pack;
                    }
                }
                catch
                {
                    // Ignore unreadable external language pack so startup stays resilient.
                }
            }
        }

        return packsByCode.Values
            .OrderBy(p => string.Equals(p.Definition.Code, "fa-IR", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Definition.NativeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static LanguagePack? TryDeserializeStream(Stream stream)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<LanguagePackDto>(stream, JsonOptions);
            if (dto is null ||
                string.IsNullOrWhiteSpace(dto.Code) ||
                string.IsNullOrWhiteSpace(dto.NativeName) ||
                dto.Strings is null)
            {
                return null;
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in dto.Strings)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                {
                    strings[pair.Key.Trim()] = pair.Value;
                }
            }

            return new LanguagePack
            {
                Definition = new LanguageDefinition(dto.Code.Trim(), dto.NativeName.Trim(), dto.IsRightToLeft),
                Strings = strings
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed class LanguagePackDto
    {
        public string? Code { get; set; }
        public string? NativeName { get; set; }
        public bool IsRightToLeft { get; set; }
        public Dictionary<string, string?>? Strings { get; set; }
    }
}
