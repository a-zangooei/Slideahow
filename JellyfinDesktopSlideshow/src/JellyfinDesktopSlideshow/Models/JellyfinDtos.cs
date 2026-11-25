using System.Text.Json.Serialization;

namespace JellyfinDesktopSlideshow.Models;

internal sealed class PublicSystemInfoDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("ServerName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("ProductName")]
    public string? ProductName { get; set; }

    [JsonPropertyName("StartupWizardCompleted")]
    public bool? StartupWizardCompleted { get; set; }
}

internal sealed class AuthenticateByNameRequestDto
{
    [JsonPropertyName("Username")]
    public required string Username { get; init; }

    [JsonPropertyName("Pw")]
    public required string Pw { get; init; }
}

internal sealed class AuthenticationResultDto
{
    [JsonPropertyName("User")]
    public UserDto? User { get; set; }

    [JsonPropertyName("AccessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("ServerId")]
    public string? ServerId { get; set; }
}

internal sealed class UserDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }
}

internal sealed class BaseItemQueryResultDto
{
    [JsonPropertyName("Items")]
    public List<BaseItemDto>? Items { get; set; }

    [JsonPropertyName("TotalRecordCount")]
    public int TotalRecordCount { get; set; }
}

internal sealed class BaseItemDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("CollectionType")]
    public string? CollectionType { get; set; }

    [JsonPropertyName("ProductionYear")]
    public int? ProductionYear { get; set; }

    [JsonPropertyName("BackdropImageTags")]
    public List<string>? BackdropImageTags { get; set; }
}

internal sealed class SessionInfoDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Client")]
    public string? Client { get; set; }

    [JsonPropertyName("DeviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("DeviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("UserId")]
    public string? UserId { get; set; }

    [JsonPropertyName("SupportsRemoteControl")]
    public bool SupportsRemoteControl { get; set; }
}
