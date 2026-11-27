using System.Globalization;
using System.Windows;

namespace JellyfinDesktopSlideshow.Localization;

public static class BidiTextFormatter
{
    public const char LeftToRightIsolate = '\u2066';
    public const char RightToLeftIsolate = '\u2067';
    public const char FirstStrongIsolate = '\u2068';
    public const char PopDirectionalIsolate = '\u2069';

    public static FlowDirection DetectFlowDirection(string? text, FlowDirection fallback = FlowDirection.LeftToRight)
    {
        var detectedRtl = TryDetectRtlFirstStrong(text);
        return detectedRtl switch
        {
            true => FlowDirection.RightToLeft,
            false => FlowDirection.LeftToRight,
            null => fallback
        };
    }

    public static bool? TryDetectRtlFirstStrong(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var ch in text)
        {
            if (IsDirectionalControl(ch))
            {
                continue;
            }

            if (IsRtlCharacter(ch))
            {
                return true;
            }

            if (IsLtrStrongCharacter(ch))
            {
                return false;
            }
        }

        return null;
    }

    public static string Isolate(string? text, bool fallbackRtl = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (IsAlreadyIsolated(text))
        {
            return text;
        }

        var isRtl = TryDetectRtlFirstStrong(text) ?? fallbackRtl;
        var openIsolate = isRtl ? RightToLeftIsolate : LeftToRightIsolate;
        return $"{openIsolate}{text}{PopDirectionalIsolate}";
    }

    public static string IsolateLtr(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text[0] == LeftToRightIsolate && text[^1] == PopDirectionalIsolate)
        {
            return text;
        }

        return $"{LeftToRightIsolate}{text}{PopDirectionalIsolate}";
    }

    public static string FormatTitle(string? title, int? productionYear)
    {
        var cleanTitle = (title ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanTitle))
        {
            return string.Empty;
        }

        var combined = productionYear.HasValue
            ? $"{cleanTitle} ({productionYear.Value})"
            : cleanTitle;

        return Isolate(combined, fallbackRtl: false);
    }

    public static string FormatServerDetails(string? serverName, string? version, string versionLabel)
    {
        if (string.IsNullOrWhiteSpace(serverName) && string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var isolatedServer = Isolate(serverName?.Trim() ?? string.Empty, fallbackRtl: false);
        var isolatedVersion = IsolateLtr(version?.Trim() ?? string.Empty);
        if (string.IsNullOrEmpty(isolatedVersion))
        {
            return isolatedServer;
        }

        return $"{isolatedServer} — {versionLabel} {isolatedVersion}";
    }

    private static bool IsAlreadyIsolated(string text)
        => text.Length >= 2 &&
           text[^1] == PopDirectionalIsolate &&
           (text[0] == LeftToRightIsolate || text[0] == RightToLeftIsolate || text[0] == FirstStrongIsolate);

    private static bool IsDirectionalControl(char ch)
        => ch is LeftToRightIsolate or RightToLeftIsolate or FirstStrongIsolate or PopDirectionalIsolate
            or '\u200E' or '\u200F' or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E';

    private static bool IsRtlCharacter(char ch)
        => (ch >= '\u0590' && ch <= '\u05FF') || // Hebrew
           (ch >= '\u0600' && ch <= '\u06FF') || // Arabic / Persian
           (ch >= '\u0700' && ch <= '\u074F') || // Syriac
           (ch >= '\u0750' && ch <= '\u077F') || // Arabic Supplement
           (ch >= '\u0780' && ch <= '\u07BF') || // Thaana
           (ch >= '\u08A0' && ch <= '\u08FF') || // Arabic Extended-A
           (ch >= '\uFB50' && ch <= '\uFDFF') || // Arabic Presentation Forms-A
           (ch >= '\uFE70' && ch <= '\uFEFF');   // Arabic Presentation Forms-B

    private static bool IsLtrStrongCharacter(char ch)
    {
        if (IsRtlCharacter(ch))
        {
            return false;
        }

        if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(ch);
        return category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter;
    }
}
