using System.Text.Json.Serialization;

namespace libgwmapi.DTO.UserAuth;

public class LoginAccountRequest
{
    [JsonPropertyName("account")]
    public string Account { get; set; }

    [JsonPropertyName("agreement")]
    public int[] Agreement { get; set; } = { 1, 2, 23 };

    [JsonPropertyName("appType")]
    public int AppType { get; set; } = 0;

    [JsonPropertyName("country")]
    public string Country { get; set; }

    // Optional. For Russia e-mail login this must be omitted: sending countryCode="RU"
    // makes rus-h5-gateway validate account as a phone ("Телефон в 8-32 номерах").
    // When present in APK responses it is the dialing code (e.g. "7"), not ISO2.
    [JsonPropertyName("countryCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string CountryCode { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; }

    [JsonPropertyName("isEncrypt")]
    public bool IsEncrypt { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("pushToken")]
    public string PushToken { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; } = 1;
}