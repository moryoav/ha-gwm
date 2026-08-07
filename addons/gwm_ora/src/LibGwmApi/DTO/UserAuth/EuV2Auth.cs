using System.Text.Json.Serialization;

namespace libgwmapi.DTO.UserAuth;

public sealed class EuLoginWithPasswordRequest
{
    [JsonPropertyName("account")]
    public string Account { get; set; }

    [JsonPropertyName("accountType")]
    public string AccountType { get; set; } = "2";

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; }

    [JsonPropertyName("agreement")]
    public int[] Agreement { get; set; } = [1, 2];

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; }

    [JsonPropertyName("appType")]
    public string AppType { get; set; } = "0";

    [JsonPropertyName("pushToken")]
    public string PushToken { get; set; } = String.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("verifyCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifyCode { get; set; }

    [JsonPropertyName("validCodeMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ValidCodeMode { get; set; }
}

public sealed class EuGetVerifyCodeRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "17";

    [JsonPropertyName("account")]
    public string Account { get; set; }

    [JsonPropertyName("accountType")]
    public string AccountType { get; set; } = "2";

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; }

    [JsonPropertyName("validCodeMode")]
    public int ValidCodeMode { get; set; } = 1;

    [JsonPropertyName("operateCode")]
    public string OperateCode { get; set; } = String.Empty;

    [JsonPropertyName("captchaType")]
    public string CaptchaType { get; set; } = String.Empty;

    [JsonPropertyName("captchaId")]
    public string CaptchaId { get; set; } = String.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = String.Empty;
}

public sealed class EuCheckVerifyCodeRequest
{
    [JsonPropertyName("account")]
    public string Account { get; set; }

    [JsonPropertyName("verifyCode")]
    public string VerifyCode { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "17";

    [JsonPropertyName("accountType")]
    public string AccountType { get; set; } = "2";

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; }

    [JsonPropertyName("validCodeMode")]
    public int ValidCodeMode { get; set; } = 1;
}
