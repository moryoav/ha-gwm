using System.Text.Json.Serialization;

namespace libgwmapi.DTO.UserAuth;

/// <summary>
/// AU/NZ (aus) checkSMSCode body — validates the emailed login code before the final
/// loginAccount+verifyCode call. type is the string "17" (matches getSMSCode).
/// </summary>
public class CheckSmsCode
{
    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("smsCode")]
    public string SmsCode { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "17";
}
