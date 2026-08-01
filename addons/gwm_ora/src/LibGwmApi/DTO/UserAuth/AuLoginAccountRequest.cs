using System.Text.Json.Serialization;

namespace libgwmapi.DTO.UserAuth;

/// <summary>
/// AU/NZ (aus) loginAccount body. Differs from the EU <see cref="LoginAccountRequest"/>:
/// password is PLAINTEXT, agreement is [1,2], appType is the string "0", the SMS code goes
/// in <c>verifyCode</c> (present only on the second, post-code call), and it carries the
/// null placeholder fields the app sends. Captured live 2026-07-20 (see gwm-au-login-SOLVED.md).
/// </summary>
public class AuLoginAccountRequest
{
    [JsonPropertyName("account")]
    public string Account { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("agreement")]
    public int[] Agreement { get; set; } = { 1, 2 };

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; }

    [JsonPropertyName("appType")]
    public string AppType { get; set; } = "0";

    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("accountId")]
    public string AccountId { get; set; }

    [JsonPropertyName("uid")]
    public string Uid { get; set; }

    [JsonPropertyName("smsCode")]
    public string SmsCode { get; set; }

    [JsonPropertyName("pushToken")]
    public string PushToken { get; set; } = String.Empty;

    [JsonPropertyName("loginEmail")]
    public string LoginEmail { get; set; }

    // Present only on the second loginAccount call (after the code); omitted on the first.
    [JsonPropertyName("verifyCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string VerifyCode { get; set; }
}
