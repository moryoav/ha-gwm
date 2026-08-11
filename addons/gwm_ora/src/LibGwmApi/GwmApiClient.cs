using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace libgwmapi;

public partial class GwmApiClient
{
    public static readonly string H5HttpClientName = "eu-h5-gateway";
    public static readonly string AppHttpClientName = "eu-app-gateway";

    private readonly HttpClient _h5Client;
    private readonly HttpClient _authClient;
    private readonly HttpClient _appClient;
    private readonly HttpClient _certificateClient;
    private readonly Action<X509Certificate2>? _setVehicleClientCertificate;
    private readonly ILogger<GwmApiClient> _logger;
    private readonly string _region;
    private string _deviceId = String.Empty;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public GwmApiClient(IHttpClientFactory factory, ILoggerFactory loggerFactory)
        : this(
            factory.CreateClient(H5HttpClientName),
            factory.CreateClient(H5HttpClientName),
            factory.CreateClient(AppHttpClientName),
            factory.CreateClient(AppHttpClientName),
            loggerFactory)
    {
    }

    public GwmApiClient(
        HttpClient h5Client,
        HttpClient appClient,
        ILoggerFactory loggerFactory,
        string region = "eu")
        : this(h5Client, h5Client, appClient, appClient, loggerFactory, region)
    {
    }

    public GwmApiClient(
        HttpClient h5Client,
        HttpClient authClient,
        HttpClient appClient,
        HttpClient certificateClient,
        ILoggerFactory loggerFactory,
        string region = "eu",
        Action<X509Certificate2>? setVehicleClientCertificate = null)
    {
        _region = String.IsNullOrWhiteSpace(region)
            ? "eu"
            : region.Trim().ToLowerInvariant();
        _logger = loggerFactory.CreateLogger<GwmApiClient>();
        _h5Client = h5Client;
        _authClient = authClient;
        _appClient = appClient;
        _certificateClient = certificateClient;
        _setVehicleClientCertificate = setVehicleClientCertificate;

        if (_region == "aus")
        {
            var baseUri = new Uri(
                "https://aus-h5-gateway.gwmcloud.com/app-api/api/v1.0/");
            foreach (var client in UniqueClients())
            {
                client.BaseAddress = baseUri;
                ConfigureHeader(client, "rs", "2");
                ConfigureHeader(client, "terminal", "GW_APP_Haval");
                ConfigureHeader(client, "brand", "1");
                ConfigureHeader(client, "enterpriseId", "CC01");
                ConfigureHeader(client, "appId", "1");
                ConfigureHeader(client, "channel", "APP");
                ConfigureHeader(client, "cVer", "1.0.0");
                ConfigureHeader(client, "systemType", "1");
                ConfigureHeader(client, "language", "en_US");
            }
        }
        else
        {
            _h5Client.BaseAddress = new Uri(
                $"https://{_region}-h5-gateway.gwmcloud.com/app-api/api/v1.0/");
            if (!ReferenceEquals(_authClient, _h5Client))
            {
                _authClient.BaseAddress = new Uri(
                    $"https://{_region}-h5-gateway.gwmcloud.com/app-api/api/v2.0/");
            }
            if (!ReferenceEquals(_appClient, _h5Client) &&
                !ReferenceEquals(_appClient, _authClient))
            {
                _appClient.BaseAddress = new Uri(
                    $"https://{_region}-app-gateway.gwmcloud.com/app-api/api/v1.0/");
            }
            if (!ReferenceEquals(_certificateClient, _appClient) &&
                !ReferenceEquals(_certificateClient, _h5Client) &&
                !ReferenceEquals(_certificateClient, _authClient))
            {
                _certificateClient.BaseAddress = new Uri(
                    $"https://{_region}-app-gateway-common.gwmcloud.com/app-api/api/v1.0/");
            }

            foreach (var client in UniqueClients())
            {
                ConfigureHeader(client, "rs", "2");
                ConfigureHeader(client, "terminal", "GW_APP_GWM");
                ConfigureHeader(client, "brand", "6");
                ConfigureHeader(client, "language", "en");
                ConfigureHeader(client, "systemType", "1");
                ConfigureHeader(client, "cVer", "1.3.0");
                ConfigureHeader(client, "secVersion", "2.0");
                ConfigureHeader(client, "appId", "6");
                ConfigureHeader(client, "channel", "APP");
                ConfigureHeader(client, "enterpriseId", "CC01");
            }
        }
    }

    public string Language
    {
        get => _h5Client.DefaultRequestHeaders.TryGetValues(
            "language",
            out var values)
            ? values.FirstOrDefault() ?? String.Empty
            : String.Empty;
        set
        {
            foreach (var client in UniqueClients())
            {
                ConfigureHeader(client, "language", value);
            }
        }
    }

    public string Country
    {
        get => _h5Client.DefaultRequestHeaders.TryGetValues(
            "country",
            out var values)
            ? values.FirstOrDefault() ?? String.Empty
            : String.Empty;
        set
        {
            foreach (var client in UniqueClients())
            {
                ConfigureHeader(client, "country", value);
                ConfigureHeader(client, "regionCode", value);
            }
        }
    }

    public string DeviceId
    {
        get => _deviceId;
        set
        {
            _deviceId = value ?? String.Empty;
            foreach (var client in UniqueClients())
            {
                ConfigureHeader(client, "deviceId", _deviceId);
                ConfigureHeader(client, "iccid", _deviceId);
            }
        }
    }

    public string CertificateDeviceId
    {
        set
        {
            ConfigureHeader(_certificateClient, "deviceId", value ?? String.Empty);
            ConfigureHeader(_certificateClient, "iccid", value ?? String.Empty);
        }
    }

    public bool HasAccessToken =>
        _h5Client.DefaultRequestHeaders.TryGetValues("accessToken", out var token) &&
        token.Any(value => !String.IsNullOrEmpty(value));

    public void SetAccessToken(string accessToken)
    {
        foreach (var client in UniqueClients())
        {
            client.DefaultRequestHeaders.Remove("accessToken");
            if (!String.IsNullOrWhiteSpace(accessToken))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "accessToken",
                    accessToken);
            }
        }
    }

    public void SetVehicleClientCertificate(X509Certificate2 certificate)
    {
        if (_setVehicleClientCertificate is null)
        {
            throw new InvalidOperationException(
                "This GWM API client cannot replace its vehicle client certificate.");
        }

        _setVehicleClientCertificate(certificate);
    }

    private async Task PostH5Async<T>(
        string url,
        T body,
        CancellationToken cancellationToken)
    {
        var response = await _h5Client.PostAsJsonAsync(
            url,
            body,
            cancellationToken);
        await CheckResponseAsync(response, cancellationToken);
    }

    private async Task PostAuthAsync<T>(
        string url,
        T body,
        CancellationToken cancellationToken)
    {
        var response = await _authClient.PostAsJsonAsync(
            url,
            body,
            cancellationToken);
        await CheckResponseAsync(response, cancellationToken);
    }

    private async Task<TOut> PostAuthAsync<TIn, TOut>(
        string url,
        TIn body,
        CancellationToken cancellationToken)
    {
        var response = await _authClient.PostAsJsonAsync(
            url,
            body,
            cancellationToken);
        return await GetResponseAsync<TOut>(response, cancellationToken);
    }

    private async Task<TOut> PostCertificateAsync<TIn, TOut>(
        string url,
        TIn body,
        CancellationToken cancellationToken)
    {
        var response = await _certificateClient.PostAsJsonAsync(
            url,
            body,
            cancellationToken);
        return await GetResponseAsync<TOut>(response, cancellationToken);
    }

    private async Task PostAppAsync<T>(
        string url,
        T body,
        CancellationToken cancellationToken)
    {
        var response = await _appClient.PostAsJsonAsync(
            url,
            body,
            cancellationToken);
        await CheckResponseAsync(response, cancellationToken);
    }

    private async Task<TOut> PostH5Async<TIn, TOut>(
        string url,
        TIn body,
        CancellationToken cancellationToken)
    {
        var response = await _h5Client.PostAsJsonAsync(
            url,
            body,
            cancellationToken);
        return await GetResponseAsync<TOut>(response, cancellationToken);
    }

    private async Task<T> GetH5Async<T>(
        string url,
        CancellationToken cancellationToken)
    {
        var response = await _h5Client.GetAsync(url, cancellationToken);
        return await GetResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetAppAsync<T>(
        string url,
        CancellationToken cancellationToken)
    {
        var response = await _appClient.GetAsync(url, cancellationToken);
        return await GetResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetAppAsync<T>(
        string url,
        IEnumerable<(string Name, string Value)> extraHeaders,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        var response = await _appClient.SendAsync(request, cancellationToken);
        return await GetResponseAsync<T>(response, cancellationToken);
    }

    private async Task CheckResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await ReadGwmResponseAsync<GwmResponse>(response, cancellationToken);
    }

    private async Task<T> GetResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var result = await ReadGwmResponseAsync<GwmResponse<T>>(
            response,
            cancellationToken);
        return result.Data;
    }

    private async Task<T> ReadGwmResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
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
            result = JsonSerializer.Deserialize<T>(
                content,
                _region == "aus" ? SerializerOptions : null);
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

    private static void CheckResponse(GwmResponse response)
    {
        if (response.Code != "000000")
        {
            throw new GwmApiException(response.Code, response.Description);
        }
    }

    private IEnumerable<HttpClient> UniqueClients()
    {
        return new[]
        {
            _h5Client,
            _authClient,
            _appClient,
            _certificateClient
        }.Distinct();
    }

    private static void ConfigureHeader(
        HttpClient client,
        string name,
        string value)
    {
        client.DefaultRequestHeaders.Remove(name);
        client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
    }

    private class GwmResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    private class GwmResponse<T> : GwmResponse
    {
        [JsonPropertyName("data")]
        public T Data { get; set; }
    }

    private class GwmArrayResponse<T> : GwmResponse
    {
        [JsonPropertyName("data")]
        public T[] Data { get; set; }
    }
}
