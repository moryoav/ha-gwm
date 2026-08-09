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
