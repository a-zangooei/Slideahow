using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JellyfinDesktopSlideshow.Infrastructure;
using JellyfinDesktopSlideshow.Models;

namespace JellyfinDesktopSlideshow.Services;

public sealed class JellyfinClient : IJellyfinClient
{
    private const string ClientName = "JellyfinDesktopSlideshow";
    private const string ClientVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILoggingService _logger;

    public JellyfinClient(HttpClient httpClient, ILoggingService logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ServerPublicInfo> GetPublicServerInfoAsync(Uri serverUri, CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(serverUri, "System/Info/Public");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "برقراری ارتباط با سرور Jellyfin ناموفق بود.");

        var dto = await DeserializeAsync<PublicSystemInfoDto>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Version))
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.InvalidResponse,
                "پاسخ سرور در مسیر /System/Info/Public ناقص یا غیرمنتظره است.");
        }

        return new ServerPublicInfo
        {
            ServerId = dto.Id,
            ServerName = string.IsNullOrWhiteSpace(dto.ServerName) ? serverUri.Host : dto.ServerName,
            Version = dto.Version,
            ProductName = dto.ProductName,
            StartupWizardCompleted = dto.StartupWizardCompleted
        };
    }

    public async Task<AuthResult> AuthenticateByNameAsync(
        Uri serverUri,
        string username,
        string password,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var requestUri = new Uri(serverUri, "Users/AuthenticateByName");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new AuthenticateByNameRequestDto
            {
                Username = username.Trim(),
                Pw = password
            })
        };

        ApplyMediaBrowserAuthorization(request, deviceId, accessToken: null);

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.Unauthorized,
                "نام کاربری یا رمز عبور اشتباه است.",
                response.StatusCode);
        }

        EnsureSuccess(response, "ورود به حساب Jellyfin ناموفق بود.");

        var dto = await DeserializeAsync<AuthenticationResultDto>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(dto.AccessToken) ||
            dto.User is null ||
            string.IsNullOrWhiteSpace(dto.User.Id))
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.InvalidResponse,
                "پاسخ ورود سرور فاقد توکن یا شناسهٔ کاربر است.");
        }

        return new AuthResult
        {
            UserId = dto.User.Id,
            Username = dto.User.Name ?? username.Trim(),
            AccessToken = dto.AccessToken,
            ServerId = dto.ServerId ?? string.Empty
        };
    }

    public async Task<bool> ValidateSessionAsync(
        Uri serverUri,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(serverUri, "Users/Me");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyMediaBrowserAuthorization(request, deviceId, accessToken);

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        EnsureSuccess(response, "اعتبارسنجی نشست کاربر ناموفق بود.");
        return true;
    }

    public async Task<bool> ValidateApiKeyAsync(
        Uri serverUri,
        string apiKey,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var requestUri = new Uri(serverUri, "System/Info");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyMediaBrowserAuthorization(request, deviceId, apiKey);

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        EnsureSuccess(response, "اعتبارسنجی کلید API ناموفق بود.");
        return true;
    }

    public async Task<IReadOnlyList<JellyfinLibrary>> GetUserViewsAsync(
        Uri serverUri,
        string? userId,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var primaryRoute = string.IsNullOrWhiteSpace(userId)
            ? "Library/MediaFolders"
            : $"Users/{Uri.EscapeDataString(userId)}/Views";

        var primaryUri = new Uri(serverUri, primaryRoute);
        using var primaryRequest = new HttpRequestMessage(HttpMethod.Get, primaryUri);
        ApplyMediaBrowserAuthorization(primaryRequest, deviceId, accessToken);

        using var primaryResponse = await SendAsync(primaryRequest, cancellationToken);
        HttpResponseMessage activeResponse = primaryResponse;
        HttpResponseMessage? fallbackResponse = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(userId) && primaryResponse.StatusCode == HttpStatusCode.NotFound)
            {
                var fallbackUri = new Uri(serverUri, $"UserViews?userId={Uri.EscapeDataString(userId)}");
                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, fallbackUri);
                ApplyMediaBrowserAuthorization(fallbackRequest, deviceId, accessToken);
                fallbackResponse = await SendAsync(fallbackRequest, cancellationToken);
                activeResponse = fallbackResponse;
            }

            EnsureSuccess(activeResponse, "دریافت فهرست کتابخانه‌ها ناموفق بود.");
            var result = await DeserializeAsync<BaseItemQueryResultDto>(activeResponse, cancellationToken);

            var libraries = new List<JellyfinLibrary>();
            if (result.Items is not null)
            {
                foreach (var item in result.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
                    {
                        continue;
                    }

                    libraries.Add(new JellyfinLibrary
                    {
                        Id = item.Id,
                        Name = item.Name,
                        CollectionType = item.CollectionType
                    });
                }
            }

            return libraries;
        }
        finally
        {
            fallbackResponse?.Dispose();
        }
    }

    public async Task<(IReadOnlyList<MediaItem> Items, int TotalRecordCount)> GetItemsAsync(
        Uri serverUri,
        string? userId,
        string accessToken,
        string deviceId,
        string? parentId,
        IReadOnlyList<string> includeItemTypes,
        int startIndex,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>
        {
            "Recursive=true",
            "Fields=ProductionYear,BackdropImageTags",
            $"StartIndex={startIndex}",
            $"Limit={limit}"
        };

        if (!string.IsNullOrWhiteSpace(parentId))
        {
            queryParts.Add($"ParentId={Uri.EscapeDataString(parentId)}");
        }

        if (includeItemTypes.Count > 0)
        {
            var joined = string.Join(",", includeItemTypes.Select(Uri.EscapeDataString));
            queryParts.Add($"IncludeItemTypes={joined}");
        }

        var itemsRoute = string.IsNullOrWhiteSpace(userId)
            ? $"Items?{string.Join("&", queryParts)}"
            : $"Users/{Uri.EscapeDataString(userId)}/Items?{string.Join("&", queryParts)}";
        var requestUri = new Uri(serverUri, itemsRoute);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyMediaBrowserAuthorization(request, deviceId, accessToken);

        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "دریافت فهرست عنوان‌های رسانه ناموفق بود.");

        var dto = await DeserializeAsync<BaseItemQueryResultDto>(response, cancellationToken);
        var items = new List<MediaItem>();

        if (dto.Items is not null)
        {
            foreach (var raw in dto.Items)
            {
                var mapped = TryMapMediaItem(serverUri, raw);
                if (mapped is not null)
                {
                    items.Add(mapped);
                }
            }
        }

        return (items, dto.TotalRecordCount);
    }

    public async Task<IReadOnlyList<MediaItem>> GetCollectionItemsAsync(
        Uri serverUri,
        string? userId,
        string accessToken,
        string deviceId,
        string collectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);

        const int pageSize = 200;
        var startIndex = 0;
        var uniqueMembers = new Dictionary<string, MediaItem>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var queryParts = new List<string>
            {
                $"ParentId={Uri.EscapeDataString(collectionId)}",
                "Recursive=true",
                "CollapseBoxSetItems=false",
                "Fields=ProductionYear,BackdropImageTags",
                $"StartIndex={startIndex}",
                $"Limit={pageSize}"
            };

            var itemsRoute = string.IsNullOrWhiteSpace(userId)
                ? $"Items?{string.Join("&", queryParts)}"
                : $"Users/{Uri.EscapeDataString(userId)}/Items?{string.Join("&", queryParts)}";
            var requestUri = new Uri(serverUri, itemsRoute);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            ApplyMediaBrowserAuthorization(request, deviceId, accessToken);

            using var response = await SendAsync(request, cancellationToken);
            EnsureSuccess(response, "دریافت فهرست آثار عضو کالکشن ناموفق بود.");

            var dto = await DeserializeAsync<BaseItemQueryResultDto>(response, cancellationToken);
            var batchCount = dto.Items?.Count ?? 0;

            if (dto.Items is not null)
            {
                foreach (var raw in dto.Items)
                {
                    var mapped = TryMapMediaItem(serverUri, raw);
                    if (mapped is null ||
                        string.Equals(mapped.Id, collectionId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(mapped.Type, "Season", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(mapped.Type, "Episode", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    uniqueMembers.TryAdd(mapped.Id, mapped);
                }
            }

            startIndex += batchCount;
            if (batchCount == 0 || startIndex >= dto.TotalRecordCount)
            {
                break;
            }
        }

        return uniqueMembers.Values.ToList();
    }

    private static MediaItem? TryMapMediaItem(Uri serverUri, BaseItemDto raw)
    {
        if (string.IsNullOrWhiteSpace(raw.Id) || string.IsNullOrWhiteSpace(raw.Name))
        {
            return null;
        }

        var tags = raw.BackdropImageTags ?? [];
        string? backdropUrl = null;
        if (tags.Count > 0)
        {
            backdropUrl = new Uri(
                serverUri,
                $"Items/{Uri.EscapeDataString(raw.Id)}/Images/Backdrop/0?tag={Uri.EscapeDataString(tags[0])}")
                .ToString();
        }

        return new MediaItem
        {
            Id = raw.Id,
            Name = raw.Name,
            Type = raw.Type,
            ProductionYear = raw.ProductionYear,
            BackdropImageUrl = backdropUrl,
            BackdropImageTags = tags
        };
    }

    public async Task<(byte[] Bytes, string? ContentType)> DownloadBackdropImageAsync(
        Uri serverUri,
        string itemId,
        int imageIndex,
        string? tag,
        int maxWidth,
        int quality,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var query = $"maxWidth={Math.Clamp(maxWidth, 320, 3840)}&quality={Math.Clamp(quality, 40, 100)}";
        if (!string.IsNullOrWhiteSpace(tag))
        {
            query += $"&tag={Uri.EscapeDataString(tag)}";
        }

        var requestUri = new Uri(
            serverUri,
            $"Items/{Uri.EscapeDataString(itemId)}/Images/Backdrop/{Math.Max(0, imageIndex)}?{query}");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyMediaBrowserAuthorization(request, deviceId, accessToken);

        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "دریافت تصویر Backdrop از سرور ناموفق بود.");

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        return (bytes, contentType);
    }

    public async Task<IReadOnlyList<JellyfinSessionInfo>> GetSessionsAsync(
        Uri serverUri,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(serverUri, "Sessions");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyMediaBrowserAuthorization(request, deviceId, accessToken);

        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "دریافت فهرست نشست‌های فعال ناموفق بود.");

        var dtos = await DeserializeAsync<List<SessionInfoDto>>(response, cancellationToken);
        return dtos
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => new JellyfinSessionInfo
            {
                Id = s.Id!,
                Client = s.Client,
                DeviceName = s.DeviceName,
                DeviceId = s.DeviceId,
                UserId = s.UserId,
                SupportsRemoteControl = s.SupportsRemoteControl
            })
            .ToList();
    }

    public async Task SendSessionViewingAsync(
        Uri serverUri,
        string sessionId,
        string itemId,
        string itemType,
        string? itemName,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var query = $"itemId={Uri.EscapeDataString(itemId)}&itemType={Uri.EscapeDataString(itemType)}";
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            query += $"&itemName={Uri.EscapeDataString(itemName)}";
        }

        var requestUri = new Uri(serverUri, $"Sessions/{Uri.EscapeDataString(sessionId)}/Viewing?{query}");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        ApplyMediaBrowserAuthorization(request, deviceId, accessToken);

        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "ارسال دستور بازکردن عنوان به کلاینت Jellyfin ناموفق بود.");
    }

    public async Task LogoutAsync(
        Uri serverUri,
        string accessToken,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(serverUri, "Sessions/Logout");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        ApplyMediaBrowserAuthorization(request, deviceId, accessToken);

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            EnsureSuccess(response, "خروج از نشست سرور انجام نشد.");
        }
    }

    internal static string BuildAuthorizationParameter(string deviceId, string? accessToken)
    {
        var machine = EscapeHeaderValue(Environment.MachineName);
        var cleanDeviceId = EscapeHeaderValue(deviceId);

        var header = $"Client=\"{ClientName}\", Device=\"{machine}\", DeviceId=\"{cleanDeviceId}\", Version=\"{ClientVersion}\"";
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            header += $", Token=\"{EscapeHeaderValue(accessToken)}\"";
        }

        return header;
    }

    private static void ApplyMediaBrowserAuthorization(HttpRequestMessage request, string deviceId, string? accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "MediaBrowser",
            BuildAuthorizationParameter(deviceId, accessToken));
    }

    private static string EscapeHeaderValue(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning($"Timeout در درخواست به {request.RequestUri?.AbsolutePath}");
            throw new JellyfinApiException(
                JellyfinErrorKind.Timeout,
                "پاسخ سرور بیش از حد طول کشید (Timeout).",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning($"خطای شبکه در ارتباط با {request.RequestUri?.AbsolutePath}: {ex.Message}");
            throw new JellyfinApiException(
                JellyfinErrorKind.Unreachable,
                "سرور Jellyfin در دسترس نیست. آدرس سرور و اتصال شبکه را بررسی کنید.",
                innerException: ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string fallbackMessage)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new JellyfinApiException(
                JellyfinErrorKind.Unauthorized,
                "نشست شما منقضی یا نامعتبر شده است. لطفاً دوباره وارد شوید.",
                response.StatusCode),
            HttpStatusCode.Forbidden => new JellyfinApiException(
                JellyfinErrorKind.Forbidden,
                "این حساب کاربری مجوز دسترسی به این بخش را ندارد.",
                response.StatusCode),
            HttpStatusCode.BadRequest => new JellyfinApiException(
                JellyfinErrorKind.BadRequestOrVersionMismatch,
                "درخواست با این نسخه از سرور Jellyfin سازگار نیست یا شناسه نامعتبر است.",
                response.StatusCode),
            HttpStatusCode.NotFound => new JellyfinApiException(
                JellyfinErrorKind.NotFound,
                "مسیر API در این نسخه از سرور یافت نشد.",
                response.StatusCode),
            >= HttpStatusCode.InternalServerError => new JellyfinApiException(
                JellyfinErrorKind.ServerError,
                "سرور Jellyfin خطای داخلی گزارش داد.",
                response.StatusCode),
            _ => new JellyfinApiException(
                JellyfinErrorKind.ServerError,
                $"{fallbackMessage} (کد خطا: {(int)response.StatusCode})",
                response.StatusCode)
        };
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var data = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            if (data is null)
            {
                throw new JellyfinApiException(
                    JellyfinErrorKind.InvalidResponse,
                    "پاسخ سرور خالی است.");
            }

            return data;
        }
        catch (JsonException ex)
        {
            throw new JellyfinApiException(
                JellyfinErrorKind.InvalidResponse,
                "فرمت JSON پاسخ سرور معتبر نیست.",
                response.StatusCode,
                ex);
        }
    }
}
