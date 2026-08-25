#nullable enable

using System.Text.Json.Serialization;

namespace libgwmapi.DTO.China;

/// <summary>
/// Tokens returned by the three services used by the mainland-China GWM app.
/// China support is experimental: these fields are intentionally kept separate from
/// the overseas access/refresh tokens so changing regions cannot mix credentials.
/// </summary>
public sealed class ChinaSession
{
    [JsonPropertyName("g_token")]
    public string? GToken { get; set; }

    [JsonPropertyName("g_refresh_token")]
    public string? GRefreshToken { get; set; }

    [JsonPropertyName("sso_token")]
    public string? SsoToken { get; set; }

    [JsonPropertyName("pt_token")]
    public string? PtToken { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("bean_id")]
    public string? BeanId { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("bt_access_token")]
    public string? BeanTechAccessToken { get; set; }

    [JsonPropertyName("bt_refresh_token")]
    public string? BeanTechRefreshToken { get; set; }

    [JsonPropertyName("bt_sso_token")]
    public string? BeanTechSsoToken { get; set; }

    [JsonPropertyName("bt_bean_id")]
    public string? BeanTechBeanId { get; set; }

    [JsonPropertyName("auto_ai_token_id")]
    public string? AutoAiTokenId { get; set; }

    [JsonPropertyName("auto_ai_user_id")]
    public string? AutoAiUserId { get; set; }

    [JsonPropertyName("auto_ai_gw_id")]
    public string? AutoAiGwId { get; set; }

    [JsonIgnore]
    public bool HasGAppTokens =>
        !String.IsNullOrWhiteSpace(GToken)
        && !String.IsNullOrWhiteSpace(GRefreshToken)
        && !String.IsNullOrWhiteSpace(UserId);

    [JsonIgnore]
    public bool IsComplete =>
        HasGAppTokens
        && !String.IsNullOrWhiteSpace(BeanTechAccessToken)
        && !String.IsNullOrWhiteSpace(AutoAiTokenId)
        && !String.IsNullOrWhiteSpace(AutoAiUserId);

    public ChinaSession Clone() => (ChinaSession)MemberwiseClone();
}
