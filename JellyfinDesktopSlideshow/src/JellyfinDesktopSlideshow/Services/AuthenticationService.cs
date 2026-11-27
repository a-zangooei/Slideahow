using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Localization;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IJellyfinClient _client;
    private readonly ISettingsService _settingsService;
    private readonly ISecureStorageService _secureStorage;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;

    public ConnectionSessionState CurrentSession { get; private set; } = new();

    public AuthenticationService(
        IJellyfinClient client,
        ISettingsService settingsService,
        ISecureStorageService secureStorage,
        ILoggingService logger,
        ILocalizationService? localization = null)
    {
        _client = client;
        _settingsService = settingsService;
        _secureStorage = secureStorage;
        _logger = logger;
        _localization = localization ?? AppLocalization.Current;
    }

    public async Task<ServerPublicInfo> TestConnectionAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        if (!_settingsService.TryValidateServerUrl(serverUrl, out var serverUri, out var validationError) || serverUri is null)
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.BadRequestOrVersionMismatch,
                validationError ?? _localization.GetString("Auth.Error.InvalidServerUrl"));
        }

        var info = await _client.GetPublicServerInfoAsync(serverUri, cancellationToken);
        return new ServerPublicInfo
        {
            ServerId = info.ServerId,
            ServerName = info.ServerName,
            Version = info.Version,
            ProductName = info.ProductName,
            StartupWizardCompleted = info.StartupWizardCompleted,
            IsNonLocalPlainHttp = _settingsService.IsNonLocalPlainHttp(serverUri)
        };
    }

    public async Task<ConnectionSessionState> LoginWithApiKeyAsync(
        string serverUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (!_settingsService.TryValidateServerUrl(serverUrl, out var serverUri, out var validationError) || serverUri is null)
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.BadRequestOrVersionMismatch,
                validationError ?? _localization.GetString("Auth.Error.InvalidServerUrl"));
        }

        var trimmedKey = (apiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            throw new JellyfinApiException(JellyfinErrorKind.Unauthorized, _localization.GetString("Auth.Error.EnterApiKey"));
        }

        if (trimmedKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new JellyfinApiException(JellyfinErrorKind.Unauthorized, _localization.GetString("Auth.Error.InvalidApiKeyFormat"));
        }

        var publicInfo = await _client.GetPublicServerInfoAsync(serverUri, cancellationToken);
        var settings = _settingsService.Current;
        var isValid = await _client.ValidateApiKeyAsync(serverUri, trimmedKey, settings.DeviceId, cancellationToken);
        if (!isValid)
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.Unauthorized,
                _localization.GetString("Auth.Error.InvalidOrRevokedApiKey"));
        }

        _secureStorage.SaveSecret(settings.TokenCredentialKey, "ApiKey", trimmedKey);

        settings.ServerUrl = serverUri.ToString().TrimEnd('/');
        settings.ServerId = publicInfo.ServerId;
        settings.AuthMethod = AppSettings.ApiKeyAuthMethod;
        settings.UserId = null;
        settings.Username = null;
        _settingsService.Save(settings);

        CurrentSession = new ConnectionSessionState
        {
            IsAuthenticated = true,
            ServerUri = serverUri,
            ServerName = publicInfo.ServerName,
            ServerVersion = publicInfo.Version,
            ServerId = publicInfo.ServerId,
            UserId = null,
            Username = _localization.GetString("Auth.ApiKeySessionName"),
            AccessToken = trimmedKey,
            IsApiKeySession = true,
            IsNonLocalPlainHttp = _settingsService.IsNonLocalPlainHttp(serverUri)
        };

        _logger.Info($"اتصال موفق با کلید API به سرور {publicInfo.ServerName} ({publicInfo.Version}).");
        return CurrentSession;
    }

    public async Task<ConnectionSessionState> LoginAsync(
        string serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!_settingsService.TryValidateServerUrl(serverUrl, out var serverUri, out var validationError) || serverUri is null)
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.BadRequestOrVersionMismatch,
                validationError ?? _localization.GetString("Auth.Error.InvalidServerUrl"));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new JellyfinApiException(JellyfinErrorKind.Unauthorized, _localization.GetString("Auth.Error.EnterUsername"));
        }

        var publicInfo = await _client.GetPublicServerInfoAsync(serverUri, cancellationToken);
        var settings = _settingsService.Current;
        var auth = await _client.AuthenticateByNameAsync(serverUri, username, password, settings.DeviceId, cancellationToken);

        var serverId = string.IsNullOrWhiteSpace(auth.ServerId) ? publicInfo.ServerId : auth.ServerId;

        _secureStorage.SaveSecret(settings.TokenCredentialKey, auth.Username, auth.AccessToken);

        settings.ServerUrl = serverUri.ToString().TrimEnd('/');
        settings.ServerId = serverId;
        settings.AuthMethod = AppSettings.PasswordAuthMethod;
        settings.UserId = auth.UserId;
        settings.Username = auth.Username;
        _settingsService.Save(settings);

        CurrentSession = new ConnectionSessionState
        {
            IsAuthenticated = true,
            ServerUri = serverUri,
            ServerName = publicInfo.ServerName,
            ServerVersion = publicInfo.Version,
            ServerId = serverId,
            UserId = auth.UserId,
            Username = auth.Username,
            AccessToken = auth.AccessToken,
            IsApiKeySession = false,
            IsNonLocalPlainHttp = _settingsService.IsNonLocalPlainHttp(serverUri)
        };

        _logger.Info($"ورود موفق برای کاربر {auth.Username} به سرور {publicInfo.ServerName} ({publicInfo.Version}).");
        return CurrentSession;
    }

    public async Task<ConnectionSessionState> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Current;
        if (string.IsNullOrWhiteSpace(settings.ServerUrl) ||
            !_settingsService.TryValidateServerUrl(settings.ServerUrl, out var serverUri, out _) ||
            serverUri is null)
        {
            CurrentSession = new ConnectionSessionState { IsAuthenticated = false };
            return CurrentSession;
        }

        var isApiKeySession = string.Equals(settings.AuthMethod, AppSettings.ApiKeyAuthMethod, StringComparison.OrdinalIgnoreCase) ||
                              string.IsNullOrWhiteSpace(settings.UserId);

        if (!isApiKeySession && string.IsNullOrWhiteSpace(settings.UserId))
        {
            CurrentSession = new ConnectionSessionState { IsAuthenticated = false, ServerUri = serverUri };
            return CurrentSession;
        }

        var token = _secureStorage.ReadSecret(settings.TokenCredentialKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            CurrentSession = new ConnectionSessionState { IsAuthenticated = false, ServerUri = serverUri };
            return CurrentSession;
        }

        try
        {
            var publicInfo = await _client.GetPublicServerInfoAsync(serverUri, cancellationToken);
            var isValid = isApiKeySession
                ? await _client.ValidateApiKeyAsync(serverUri, token, settings.DeviceId, cancellationToken)
                : await _client.ValidateSessionAsync(serverUri, token, settings.DeviceId, cancellationToken);

            if (!isValid)
            {
                _secureStorage.DeleteSecret(settings.TokenCredentialKey);
                CurrentSession = new ConnectionSessionState
                {
                    IsAuthenticated = false,
                    ServerUri = serverUri,
                    ServerName = publicInfo.ServerName,
                    ServerVersion = publicInfo.Version,
                    WarningOrError = isApiKeySession
                        ? _localization.GetString("Auth.Error.StoredApiKeyInvalid")
                        : _localization.GetString("Auth.Error.StoredTokenExpired")
                };
                return CurrentSession;
            }

            CurrentSession = new ConnectionSessionState
            {
                IsAuthenticated = true,
                ServerUri = serverUri,
                ServerName = publicInfo.ServerName,
                ServerVersion = publicInfo.Version,
                ServerId = string.IsNullOrWhiteSpace(settings.ServerId) ? publicInfo.ServerId : settings.ServerId,
                UserId = isApiKeySession ? null : settings.UserId,
                Username = isApiKeySession ? _localization.GetString("Auth.ApiKeySessionName") : settings.Username,
                AccessToken = token,
                IsApiKeySession = isApiKeySession,
                IsNonLocalPlainHttp = _settingsService.IsNonLocalPlainHttp(serverUri)
            };
            return CurrentSession;
        }
        catch (JellyfinApiException ex)
        {
            _logger.Warning($"بازیابی نشست ناموفق بود: {ex.Message}");
            CurrentSession = new ConnectionSessionState
            {
                IsAuthenticated = false,
                ServerUri = serverUri,
                WarningOrError = ex.Message
            };
            return CurrentSession;
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Current;
        var token = CurrentSession.AccessToken ?? _secureStorage.ReadSecret(settings.TokenCredentialKey);
        var isApiKeySession = CurrentSession.IsApiKeySession ||
                              string.Equals(settings.AuthMethod, AppSettings.ApiKeyAuthMethod, StringComparison.OrdinalIgnoreCase);

        if (!isApiKeySession && CurrentSession.ServerUri is not null && !string.IsNullOrWhiteSpace(token))
        {
            try
            {
                await _client.LogoutAsync(CurrentSession.ServerUri, token, settings.DeviceId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Warning($"خروج از سرور کامل نشد (توکن محلی حذف می‌شود): {ex.Message}");
            }
        }

        _secureStorage.DeleteSecret(settings.TokenCredentialKey);
        CurrentSession = new ConnectionSessionState
        {
            IsAuthenticated = false,
            ServerUri = CurrentSession.ServerUri
        };
        _logger.Info(isApiKeySession
            ? "قطع اتصال انجام شد و کلید API ذخیره‌شده از حافظهٔ امن پاک گردید."
            : "خروج از حساب انجام شد و توکن محلی پاک گردید.");
    }
}
