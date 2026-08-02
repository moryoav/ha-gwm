using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace libgwmapi;

public partial class GwmApiClient
{
    public static readonly string H5HttpClientName = "eu-h5-gateway";
    public static readonly string AppHttpClientName = "eu-app-gateway";
    private readonly HttpClient _h5Client;
    private readonly HttpClient _appClient;
    private readonly ILogger<GwmApiClient> _logger;
    private readonly string _region;
    private string _deviceId = String.Empty;

    // The AU/NZ gateway is loosely typed and returns some numeric fields as JSON strings
    // (e.g. "securityTime":"0"). This lenient option is selected for aus responses only;
    // EU responses continue to use the default strict deserializer.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public GwmApiClient(IHttpClientFactory factory, ILoggerFactory loggerFactory)
        : this(factory.CreateClient(H5HttpClientName), factory.CreateClient(AppHttpClientName), loggerFactory)
    {
    }

    public GwmApiClient(HttpClient h5Client, HttpClient appClient, ILoggerFactory loggerFactory, string region = "eu")
    {
        _region = string.IsNullOrWhiteSpace(region) ? "eu" : region.Trim().ToLowerInvariant();
        _logger = loggerFactory.CreateLogger<GwmApiClient>();
        _h5Client = h5Client;
        _appClient = appClient;

        if (_region == "aus")
        {
            // AU/NZ: the app-gateway is dead (connection refused); everything goes through the
            // h5-gateway, authenticated by bt-auth request signing (see BtAuthSigningHandler)
            // rather than the EU mutual-TLS client certificate.
            var baseUri = new Uri("https://aus-h5-gateway.gwmcloud.com/app-api/api/v1.0/");
            foreach (var c in new[] { _h5Client, _appClient })
            {
                c.BaseAddress = baseUri;
                c.DefaultRequestHeaders.Add("rs", "2");
                c.DefaultRequestHeaders.Add("terminal", "GW_APP_Haval");
                c.DefaultRequestHeaders.Add("brand", "1");
                c.DefaultRequestHeaders.Add("enterpriseId", "CC01");
                c.DefaultRequestHeaders.Add("appId", "1");
                c.DefaultRequestHeaders.Add("channel", "APP");
                c.DefaultRequestHeaders.Add("cVer", "1.0.0");
                c.DefaultRequestHeaders.Add("systemType", "1");
                c.DefaultRequestHeaders.Add("language", "en_US");
            }
        }
        else
        {
            _h5Client.DefaultRequestHeaders.Add("rs", "2");
            _h5Client.DefaultRequestHeaders.Add("terminal", "GW_APP_ORA");
            _h5Client.DefaultRequestHeaders.Add("brand", "3");
            _h5Client.DefaultRequestHeaders.Add("language", "en");
            _h5Client.DefaultRequestHeaders.Add("systemType", "1");
            _h5Client.DefaultRequestHeaders.Add("cver", "");
            _h5Client.BaseAddress = new Uri($"https://{_region}-h5-gateway.gwmcloud.com/app-api/api/v1.0/");

            _appClient.DefaultRequestHeaders.Add("rs", "2");
            _appClient.DefaultRequestHeaders.Add("terminal", "GW_APP_ORA");
            _appClient.DefaultRequestHeaders.Add("brand", "3");
            _appClient.BaseAddress = new Uri($"https://{_region}-app-gateway.gwmcloud.com/app-api/api/v1.0/");
        }
    }

    public string Language
    {
        get => _h5Client.DefaultRequestHeaders.GetValues("language").FirstOrDefault();
        set
        {
            _h5Client.DefaultRequestHeaders.Remove("language");
            _h5Client.DefaultRequestHeaders.Add("language", value);
        }
    }

    public string Country
    {
        get => _h5Client.DefaultRequestHeaders.GetValues("country").FirstOrDefault();
        set
        {
            foreach (var c in new[] { _h5Client, _appClient })
            {
                c.DefaultRequestHeaders.Remove("country");
                c.DefaultRequestHeaders.Add("country", value);
                if (_region == "aus")
                {
                    c.DefaultRequestHeaders.Remove("regionCode");
                    c.DefaultRequestHeaders.Add("regionCode", value);
                }
            }
        }
    }

    // AU/NZ sends the device id (and matching iccid) as headers on every call; EU sends it
    // in the request body only, so these headers are set for aus only.
    public string DeviceId
    {
        get => _deviceId;
        set
        {
            _deviceId = value ?? String.Empty;
            if (_region != "aus")
            {
                return;
            }
            foreach (var c in new[] { _h5Client, _appClient })
            {
                c.DefaultRequestHeaders.Remove("deviceId");
                c.DefaultRequestHeaders.Add("deviceId", _deviceId);
                c.DefaultRequestHeaders.Remove("iccid");
                c.DefaultRequestHeaders.Add("iccid", _deviceId);
            }
        }
    }

    public bool HasAccessToken
    {
        get
        {
            if (!_h5Client.DefaultRequestHeaders.TryGetValues("accessToken", out var token))
                return false;
            return token.Any(x => !String.IsNullOrEmpty(x));
        }
    }

    public void SetAccessToken(string accessToken)
    {
        _h5Client.DefaultRequestHeaders.Remove("accessToken");
        _h5Client.DefaultRequestHeaders.Add("accessToken", accessToken);

        _appClient.DefaultRequestHeaders.Remove("accessToken");
        _appClient.DefaultRequestHeaders.Add("accessToken", accessToken);
    }

    private async Task PostH5Async<T>(string url, T body, CancellationToken cancellationToken)
    {
        var response = await _h5Client.PostAsJsonAsync(url, body, cancellationToken);
        await CheckResponseAsync(response, cancellationToken);
    }

    private async Task PostAppAsync<T>(string url, T body, CancellationToken cancellationToken)
    {
        var response = await _appClient.PostAsJsonAsync(url, body, cancellationToken);
        await CheckResponseAsync(response, cancellationToken);
    }

    private async Task<TOut> PostH5Async<TIn, TOut>(string url, TIn body, CancellationToken cancellationToken)
    {
        var response = await _h5Client.PostAsJsonAsync(url, body, cancellationToken);
        return await GetResponseAsync<TOut>(response, cancellationToken);
    }

    private async Task<T> GetH5Async<T>(string url, CancellationToken cancellationToken)
    {
        var response = await _h5Client.GetAsync(url, cancellationToken);
        return await GetResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetAppAsync<T>(string url, CancellationToken cancellationToken)
    {
        var response = await _appClient.GetAsync(url, cancellationToken);
        return await GetResponseAsync<T>(response, cancellationToken);
    }

    private async Task CheckResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await ReadGwmResponseAsync<GwmResponse>(response, cancellationToken);
    }

    private async Task<T> GetResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await ReadGwmResponseAsync<GwmResponse<T>>(response, cancellationToken);
        return result.Data;
    }

    private async Task<T> ReadGwmResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : GwmResponse
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(content);
        }

        T result;
        try
        {
            // aus-only leniency; EU keeps the default deserializer (null == original behaviour).
            result = JsonSerializer.Deserialize<T>(content, _region == "aus" ? SerializerOptions : null);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }

        if (result is null)
        {
            response.EnsureSuccessStatusCode();
            throw new JsonException("GWM response body was empty.");
        }

        CheckResponse(result);
        response.EnsureSuccessStatusCode();
        return result;
    }

    private void CheckResponse(GwmResponse response)
    {
        if (response.Code != "000000")
        {
            throw new GwmApiException(response.Code, response.Description);
        }
    }

    private class GwmResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    private class GwmResponse<T>:GwmResponse
    {

        [JsonPropertyName("data")]
        public T Data { get; set; }
    }

    private class GwmArrayResponse<T>:GwmResponse
    {

        [JsonPropertyName("data")]
        public T[] Data { get; set; }
    }
}
