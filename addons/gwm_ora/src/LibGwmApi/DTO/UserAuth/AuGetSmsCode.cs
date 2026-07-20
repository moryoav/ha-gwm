using System.Text.Json.Serialization;

namespace libgwmapi.DTO.UserAuth;

/// <summary>
/// AU/NZ (aus) getSMSCode body for the new-device login code. type is the string "17"
/// (the login-verification scenario), unlike the EU <see cref="GetSmsCode"/> (int type 3).
/// </summary>
public class AuGetSmsCode
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "17";

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }
}
