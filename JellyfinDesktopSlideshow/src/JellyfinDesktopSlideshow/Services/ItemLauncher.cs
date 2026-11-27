using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class ItemLauncher : IItemLauncher
{
    private readonly IJellyfinClient _client;
    private readonly IAuthenticationService _authService;
    private readonly ISettingsService _settingsService;
    private readonly IExternalLauncher _externalLauncher;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;

    public ItemLauncher(
        IJellyfinClient client,
        IAuthenticationService authService,
        ISettingsService settingsService,
        IExternalLauncher externalLauncher,
        ILoggingService logger,
        ILocalizationService? localization = null)
    {
        _client = client;
        _authService = authService;
        _settingsService = settingsService;
        _externalLauncher = externalLauncher;
        _logger = logger;
        _localization = localization ?? AppLocalization.Current;
    }

    public async Task<ItemLaunchResult> OpenItemAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var session = _authService.CurrentSession;
        var serverUri = session.ServerUri;
        if (serverUri is null &&
            !_settingsService.TryValidateServerUrl(_settingsService.Current.ServerUrl, out serverUri, out _))
        {
            return new ItemLaunchResult(false, _localization.GetString("Auth.Error.InvalidServerUrl"));
        }

        var serverId = session.ServerId ?? _settingsService.Current.ServerId;
        var webDetailsUri = BuildWebDetailsUri(serverUri!, item.Id, serverId);
        var target = _settingsService.Current.PlaybackTarget;

        return target.TargetType switch
        {
            PlaybackTargetType.DefaultBrowser => OpenInBrowser(webDetailsUri, item.Name),
            PlaybackTargetType.JellyfinDesktopClient => await OpenInDesktopClientAsync(
                serverUri!,
                session,
                item,
                webDetailsUri,
                cancellationToken),
            PlaybackTargetType.CustomUrlTemplate => OpenCustomUrl(
                target.CustomUrlTemplate,
                serverUri!,
                item,
                serverId,
                webDetailsUri),
            _ => OpenInBrowser(webDetailsUri, item.Name)
        };
    }

    public bool OpenValidatedBrowserUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        return _externalLauncher.OpenHttpUrl(uri);
    }

    public Uri BuildWebDetailsUri(Uri serverUri, string itemId, string? serverId)
    {
        var baseUrl = serverUri.ToString().TrimEnd('/');
        var fragment = $"#!/details?id={Uri.EscapeDataString(itemId)}";
        if (!string.IsNullOrWhiteSpace(serverId))
        {
            fragment += $"&serverId={Uri.EscapeDataString(serverId)}";
        }

        return new Uri($"{baseUrl}/web/index.html{fragment}", UriKind.Absolute);
    }

    public bool TryValidateCustomUrlTemplate(string? template, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(template))
        {
            errorMessage = _localization.GetString("Validation.CustomUrl.Empty");
            return false;
        }

        var trimmed = template.Trim();
        if (trimmed.Any(char.IsControl) || trimmed.Contains(' ') || trimmed.Contains('"') || trimmed.Contains('\''))
        {
            errorMessage = _localization.GetString("Validation.CustomUrl.InvalidChars");
            return false;
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("{baseUrl}", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = _localization.GetString("Validation.CustomUrl.InvalidScheme");
            return false;
        }

        var testExpanded = ExpandTemplate(
            trimmed,
            new Uri("https://jellyfin.local/"),
            "00000000000000000000000000000000",
            "11111111111111111111111111111111");

        if (!Uri.TryCreate(testExpanded, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            errorMessage = _localization.GetString("Validation.CustomUrl.InvalidExpanded");
            return false;
        }

        return true;
    }

    private ItemLaunchResult OpenInBrowser(Uri uri, string itemName)
    {
        var opened = _externalLauncher.OpenHttpUrl(uri);
        return opened
            ? new ItemLaunchResult(
                true,
                _localization.Format("Launch.Status.OpenedBrowserFormat", BidiTextFormatter.Isolate(itemName)),
                LaunchedUrl: uri.AbsoluteUri)
            : new ItemLaunchResult(false, _localization.GetString("Launch.Error.BrowserFailed"));
    }

    private async Task<ItemLaunchResult> OpenInDesktopClientAsync(
        Uri serverUri,
        ConnectionSessionState session,
        MediaItem item,
        Uri fallbackBrowserUri,
        CancellationToken cancellationToken)
    {
        if (!_externalLauncher.IsJellyfinDesktopInstalled())
        {
            return new ItemLaunchResult(
                false,
                _localization.GetString("Launch.Error.DesktopNotInstalled"),
                CanFallbackToBrowser: true,
                FallbackBrowserUrl: fallbackBrowserUri.AbsoluteUri);
        }

        if (!session.IsAuthenticated || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return new ItemLaunchResult(
                false,
                _localization.GetString("Launch.Error.DesktopNotConnected"),
                CanFallbackToBrowser: true,
                FallbackBrowserUrl: fallbackBrowserUri.AbsoluteUri);
        }

        try
        {
            var sessions = await _client.GetSessionsAsync(
                serverUri,
                session.AccessToken,
                _settingsService.Current.DeviceId,
                cancellationToken);

            var desktopSession = sessions.FirstOrDefault(s =>
                s.SupportsRemoteControl &&
                !string.Equals(s.DeviceId, _settingsService.Current.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrWhiteSpace(s.Client) &&
                 (s.Client.Contains("Jellyfin Media Player", StringComparison.OrdinalIgnoreCase) ||
                  s.Client.Contains("Jellyfin Desktop", StringComparison.OrdinalIgnoreCase)) ||
                 string.Equals(s.DeviceName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)));

            if (desktopSession is null)
            {
                return new ItemLaunchResult(
                    false,
                    _localization.GetString("Launch.Error.DesktopNoActiveSession"),
                    CanFallbackToBrowser: true,
                    FallbackBrowserUrl: fallbackBrowserUri.AbsoluteUri);
            }

            await _client.SendSessionViewingAsync(
                serverUri,
                desktopSession.Id,
                item.Id,
                string.IsNullOrWhiteSpace(item.Type) ? "Movie" : item.Type,
                item.Name,
                session.AccessToken,
                _settingsService.Current.DeviceId,
                cancellationToken);

            return new ItemLaunchResult(
                true,
                _localization.Format(
                    "Launch.Status.OpenedDesktopFormat",
                    BidiTextFormatter.Isolate(item.Name),
                    BidiTextFormatter.Isolate(desktopSession.Client ?? "Jellyfin Desktop")));
        }
        catch (JellyfinApiException ex)
        {
            _logger.Warning($"ارسال عنوان به کلاینت دسکتاپ ناموفق بود: {ex.Message}");
            return new ItemLaunchResult(
                false,
                _localization.Format("Launch.Error.DesktopFailedFormat", ex.Message),
                CanFallbackToBrowser: true,
                FallbackBrowserUrl: fallbackBrowserUri.AbsoluteUri);
        }
    }

    private ItemLaunchResult OpenCustomUrl(
        string? template,
        Uri serverUri,
        MediaItem item,
        string? serverId,
        Uri fallbackBrowserUri)
    {
        if (!TryValidateCustomUrlTemplate(template, out var validationError))
        {
            return new ItemLaunchResult(
                false,
                validationError ?? _localization.GetString("Launch.Error.InvalidCustomTemplate"),
                CanFallbackToBrowser: true,
                FallbackBrowserUrl: fallbackBrowserUri.AbsoluteUri);
        }

        var expanded = ExpandTemplate(template!.Trim(), serverUri, item.Id, serverId ?? string.Empty);
        if (!Uri.TryCreate(expanded, UriKind.Absolute, out var targetUri) ||
            (targetUri.Scheme != Uri.UriSchemeHttp && targetUri.Scheme != Uri.UriSchemeHttps))
        {
            return new ItemLaunchResult(
                false,
                _localization.GetString("Launch.Error.InvalidExpandedUrl"),
                CanFallbackToBrowser: true,
                FallbackBrowserUrl: fallbackBrowserUri.AbsoluteUri);
        }

        var opened = _externalLauncher.OpenHttpUrl(targetUri);
        return opened
            ? new ItemLaunchResult(
                true,
                _localization.Format("Launch.Status.OpenedCustomFormat", BidiTextFormatter.Isolate(item.Name)),
                LaunchedUrl: targetUri.AbsoluteUri)
            : new ItemLaunchResult(
                false,
                _localization.GetString("Launch.Error.CustomFailed"),
                CanFallbackToBrowser: true,
                FallbackBrowserUrl: fallbackBrowserUri.AbsoluteUri);
    }

    private static string ExpandTemplate(string template, Uri serverUri, string itemId, string serverId)
    {
        var baseUrl = serverUri.ToString().TrimEnd('/');
        return template
            .Replace("{baseUrl}", baseUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("{itemId}", Uri.EscapeDataString(itemId), StringComparison.OrdinalIgnoreCase)
            .Replace("{serverId}", Uri.EscapeDataString(serverId), StringComparison.OrdinalIgnoreCase);
    }
}
