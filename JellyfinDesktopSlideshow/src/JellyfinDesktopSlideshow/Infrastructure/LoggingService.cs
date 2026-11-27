using System.IO;
using System.Text.RegularExpressions;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed partial class LoggingService : ILoggingService
{
    private readonly string _logDirectory;
    private readonly Lock _syncLock = new();

    public LoggingService(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? AppDataPaths.Default.LogsDirectory;
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var filePath = Path.Combine(_logDirectory, $"app-{DateTime.UtcNow:yyyyMMdd}.log");
            var safeMessage = Sanitize(message);
            var line = $"[{DateTime.UtcNow:O}] [{level}] {safeMessage}";

            if (exception is not null)
            {
                var safeException = Sanitize($"{exception.GetType().Name}: {exception.Message}");
                line = $"{line} | {safeException}";
            }

            lock (_syncLock)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging failure must never crash the application.
        }
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var redacted = TokenRegex().Replace(input, "$1\"[REDACTED]\"");
        redacted = ApiKeyQueryRegex().Replace(redacted, "$1[REDACTED]");
        return PasswordJsonRegex().Replace(redacted, "$1\"[REDACTED]\"");
    }

    [GeneratedRegex(@"(Token\s*=\s*)""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"([?&](?:api_key|ApiKey)=)[^&\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyQueryRegex();

    [GeneratedRegex(@"(""(?:Pw|Password)""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordJsonRegex();
}
