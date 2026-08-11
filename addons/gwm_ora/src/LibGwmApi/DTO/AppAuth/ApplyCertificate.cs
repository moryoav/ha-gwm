using System.Text.Json.Serialization;

namespace libgwmapi.DTO.AppAuth;

public sealed class ApplyCertificateRequest
{
    [JsonPropertyName("csr")]
    public string Csr { get; set; }

    [JsonPropertyName("phone")]
    public string Phone { get; set; }
}

public sealed class ApplyCertificateResponse
{
    [JsonPropertyName("encoded")]
    public string Encoded { get; set; }

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }

    [JsonPropertyName("notAfter")]
    public string NotAfter { get; set; }

    [JsonPropertyName("notBefore")]
    public string NotBefore { get; set; }

    [JsonPropertyName("serialnumber")]
    public string SerialNumber { get; set; }

    [JsonPropertyName("subject")]
    public string Subject { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("createTime")]
    public string CreateTime { get; set; }
}
