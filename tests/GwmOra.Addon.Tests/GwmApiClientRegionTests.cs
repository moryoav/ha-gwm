using System.Net;
using System.Text;
using System.Text.Json;
using libgwmapi;
using Microsoft.Extensions.Logging.Abstractions;

namespace GwmOra.Addon.Tests;

public class GwmApiClientRegionTests
{
    private const string NumericStringResponse = """
    {
      "code": "000000",
      "description": "success",
      "data": {
        "securityTime": "7"
      }
    }
    """;

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task AusAcceptsNumericFieldsReturnedAsStrings()
    {
        using var h5 = new HttpClient(new JsonResponseHandler(NumericStringResponse));
        using var app = new HttpClient(new JsonResponseHandler(NumericStringResponse));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "aus");

        var user = await client.GetUserBaseInfoAsync(CancellationToken.None);

        Assert.Equal(7, user.SecurityTime);
    }

    [Fact]
    public async Task EuKeepsStrictNumericDeserialization()
    {
        using var h5 = new HttpClient(new JsonResponseHandler(NumericStringResponse));
        using var app = new HttpClient(new JsonResponseHandler(NumericStringResponse));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "eu");

        await Assert.ThrowsAsync<JsonException>(
            () => client.GetUserBaseInfoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AusKnownBasicsSignatureErrorReturnsDefaults()
    {
        const string response = """
        {"code":"607099","description":"sign is inconformity","data":null}
        """;
        using var h5 = new HttpClient(new JsonResponseHandler(response));
        using var app = new HttpClient(new JsonResponseHandler(response));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "aus");

        var basics = await client.GetVehicleBasicsInfoOrDefaultAsync(
            "VIN123",
            CancellationToken.None);

        Assert.NotNull(basics);
        Assert.Null(basics.Config);
    }

    [Fact]
    public async Task AusOtherBasicsErrorsStillPropagate()
    {
        const string response = """
        {"code":"607501","description":"logged in elsewhere","data":null}
        """;
        using var h5 = new HttpClient(new JsonResponseHandler(response));
        using var app = new HttpClient(new JsonResponseHandler(response));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "aus");

        var error = await Assert.ThrowsAsync<GwmApiException>(
            () => client.GetVehicleBasicsInfoOrDefaultAsync("VIN123", CancellationToken.None));

        Assert.Equal("607501", error.Code);
    }

    [Fact]
    public async Task EuBasicsSignatureErrorStillPropagates()
    {
        const string response = """
        {"code":"607099","description":"sign is inconformity","data":null}
        """;
        using var h5 = new HttpClient(new JsonResponseHandler(response));
        using var app = new HttpClient(new JsonResponseHandler(response));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "eu");

        var error = await Assert.ThrowsAsync<GwmApiException>(
            () => client.GetVehicleBasicsInfoOrDefaultAsync("VIN123", CancellationToken.None));

        Assert.Equal("607099", error.Code);
    }

    [Fact]
    public void RusUsesRussianGatewaysAndHavalHeaders()
    {
        using var h5 = new HttpClient();
        using var app = new HttpClient();
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "rus")
        {
            Country = "RU",
            DeviceId = "abcdef0123456789deadbeefcafebabe"
        };

        Assert.Equal(
            new Uri("https://rus-h5-gateway.gwmcloud.com/app-api/api/v1.0/"),
            h5.BaseAddress);
        Assert.Equal(
            new Uri("https://rus-app-gateway.gwmcloud.com/app-api/api/v1.0/"),
            app.BaseAddress);
        Assert.Equal("GW_APP_Haval", h5.DefaultRequestHeaders.GetValues("terminal").Single());
        Assert.Equal("1", h5.DefaultRequestHeaders.GetValues("brand").Single());
        Assert.Equal("1", h5.DefaultRequestHeaders.GetValues("appId").Single());
        Assert.Equal("APP", h5.DefaultRequestHeaders.GetValues("channel").Single());
        Assert.Equal("CCZ001", h5.DefaultRequestHeaders.GetValues("brandId").Single());
        Assert.Equal("2.0", h5.DefaultRequestHeaders.GetValues("secVersion").Single());
        Assert.Equal("ru", h5.DefaultRequestHeaders.GetValues("language").Single());
        Assert.Equal("ru", app.DefaultRequestHeaders.GetValues("language").Single());
        Assert.Equal("1", app.DefaultRequestHeaders.GetValues("communityBrand").Single());
        Assert.Equal("1.0.0", app.DefaultRequestHeaders.GetValues("cVer").Single());
        Assert.Equal("RU", h5.DefaultRequestHeaders.GetValues("regionCode").Single());
        Assert.Equal(
            "abcdef0123456789deadbeefcafebabe",
            h5.DefaultRequestHeaders.GetValues("deviceId").Single());
        Assert.Equal(
            "abcdef0123456789deadbeefcafebabe",
            h5.DefaultRequestHeaders.GetValues("iccid").Single());
        Assert.Equal(client.DeviceId, h5.DefaultRequestHeaders.GetValues("deviceId").Single());
    }

    [Fact]
    public async Task RusAcceptsNumericVehicleIdAsString()
    {
        const string response = """
        {
          "code": "000000",
          "description": "SUCCESS",
          "data": [
            {
              "vin": "TESTVIN1234567890",
              "vehicleId": 9049777052258173853,
              "modelName": "Jolion"
            }
          ]
        }
        """;
        using var h5 = new HttpClient(new JsonResponseHandler(response));
        using var app = new HttpClient(new JsonResponseHandler(response));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "rus");

        var vehicles = await client.AcquireVehiclesAsync(CancellationToken.None);

        Assert.Single(vehicles);
        Assert.Equal("TESTVIN1234567890", vehicles[0].Vin);
        Assert.Equal("9049777052258173853", vehicles[0].VehicleId);
    }

    [Fact]
    public async Task EuRejectsNumericVehicleIdForStringProperty()
    {
        const string response = """
        {
          "code": "000000",
          "description": "SUCCESS",
          "data": [
            {
              "vin": "TESTVIN1234567890",
              "vehicleId": 12345
            }
          ]
        }
        """;
        using var h5 = new HttpClient(new JsonResponseHandler(response));
        using var app = new HttpClient(new JsonResponseHandler(response));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "eu");

        await Assert.ThrowsAsync<JsonException>(
            () => client.AcquireVehiclesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AusRejectsNumericVehicleIdForStringProperty()
    {
        const string response = """
        {
          "code": "000000",
          "description": "SUCCESS",
          "data": [{"vin":"TESTVIN1234567890","vehicleId":12345}]
        }
        """;
        using var h5 = new HttpClient(new JsonResponseHandler(response));
        using var app = new HttpClient(new JsonResponseHandler(response));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "aus");

        await Assert.ThrowsAsync<JsonException>(
            () => client.AcquireVehiclesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RusRejectsBooleanForStringProperty()
    {
        const string response = """
        {
          "code": "000000",
          "description": "SUCCESS",
          "data": [{"vin":"TESTVIN1234567890","vehicleId":true}]
        }
        """;
        using var h5 = new HttpClient(new JsonResponseHandler(response));
        using var app = new HttpClient(new JsonResponseHandler(response));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "rus");

        await Assert.ThrowsAsync<JsonException>(
            () => client.AcquireVehiclesAsync(CancellationToken.None));
    }

    private sealed class CapturingHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private const string EmptyResultArray = """
    {"code":"000000","description":"success","data":[]}
    """;

    [Fact]
    public async Task AusSendsVinHeaderOnRemoteCtrlResult()
    {
        var handler = new CapturingHandler(EmptyResultArray);
        using var h5 = new HttpClient(new JsonResponseHandler(EmptyResultArray));
        using var app = new HttpClient(handler);
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "aus");

        await client.GetRemoteCtrlResultAsync("SEQ123", "ENCODED-VIN", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("vin", out var values));
        Assert.Equal("ENCODED-VIN", values!.Single());
    }

    [Fact]
    public async Task RusSendsVinHeaderOnRemoteCtrlResult()
    {
        var handler = new CapturingHandler(EmptyResultArray);
        using var h5 = new HttpClient(new JsonResponseHandler(EmptyResultArray));
        using var app = new HttpClient(handler);
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "rus");

        await client.GetRemoteCtrlResultAsync("SEQ123", "ENCODED-VIN", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("vin", out var values));
        Assert.Equal("ENCODED-VIN", values!.Single());
    }

    [Fact]
    public async Task EuDoesNotSendVinHeaderOnRemoteCtrlResult()
    {
        var handler = new CapturingHandler(EmptyResultArray);
        using var h5 = new HttpClient(new JsonResponseHandler(EmptyResultArray));
        using var app = new HttpClient(handler);
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "eu");

        await client.GetRemoteCtrlResultAsync("SEQ123", "ENCODED-VIN", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.False(handler.LastRequest!.Headers.Contains("vin"));
    }
}
