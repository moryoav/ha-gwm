using System.Net;
using System.Text;
using System.Text.Json;
using GwmOra.Addon.RemoteCommands;
using libgwmapi;
using Microsoft.Extensions.Logging.Abstractions;

namespace GwmOra.Addon.Tests;

public class GwmApiClientCommandTests
{
    private const string SuccessResponse = """
    {"code":"000000","description":"SUCCESS","data":[]}
    """;

    [Fact]
    public async Task RussianCommandsUseSecurityCheckSigningAndVinHeaders()
    {
        var requests = new List<CapturedRequest>();
        using var h5 = CreateSignedClient("h5", requests);
        using var app = CreateSignedClient("app", requests);
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, "rus")
        {
            Country = "RU",
            DeviceId = "abcdef0123456789abcdef0123456789"
        };
        var command = RemoteCommandFactory.CreateLockCommand(
            "VIN123",
            "0123456789abcdef0123456789abcdef",
            lockVehicle: true,
            useRussianProtocol: true);

        await client.ModifyVehicleRemoteCtlInfoAsync(
            RemoteCommandFactory.CreateClimateDefaults("VIN123", 22, 15),
            CancellationToken.None);
        await client.SendCmdAsync(command, CancellationToken.None);
        await client.GetRemoteCtrlResultAsync(command.SeqNo, command.Vin, CancellationToken.None);

        Assert.Collection(
            requests,
            modify =>
            {
                Assert.Equal("h5", modify.ClientName);
                Assert.Equal("/app-api/api/v1.0/vehicle/modifyVehicleRemoteCtlInfo", modify.Uri.AbsolutePath);
                AssertHeader(modify, "vin", "VIN123");
                Assert.True(modify.Headers.ContainsKey("gwm-auth-sign"));
            },
            securityCheck =>
            {
                Assert.Equal("h5", securityCheck.ClientName);
                Assert.Equal("/app-api/api/v1.0/userAuth/checkSecurityPassword", securityCheck.Uri.AbsolutePath);
                Assert.False(securityCheck.Headers.ContainsKey("vin"));
                Assert.True(securityCheck.Headers.ContainsKey("gwm-auth-sign"));
                using var json = JsonDocument.Parse(securityCheck.Body);
                Assert.Equal("3", json.RootElement.GetProperty("type").GetString());
                Assert.Equal(
                    "0123456789abcdef0123456789abcdef",
                    json.RootElement.GetProperty("securityPassword").GetString());
            },
            send =>
            {
                Assert.Equal("app", send.ClientName);
                Assert.Equal("/app-api/api/v1.0/vehicle/T5/sendCmd", send.Uri.AbsolutePath);
                AssertHeader(send, "vin", "VIN123");
                Assert.True(send.Headers.ContainsKey("gwm-auth-sign"));
                using var json = JsonDocument.Parse(send.Body);
                Assert.Equal(3, json.RootElement.GetProperty("type").GetInt32());
                Assert.Equal("VIN123", json.RootElement.GetProperty("vin").GetString());
            },
            poll =>
            {
                Assert.Equal("app", poll.ClientName);
                Assert.Equal("/app-api/api/v1.0/vehicle/getRemoteCtrlResultT5", poll.Uri.AbsolutePath);
                AssertHeader(poll, "vin", "VIN123");
                Assert.True(poll.Headers.ContainsKey("gwm-auth-sign"));
            });
    }

    [Theory]
    [InlineData("eu")]
    [InlineData("aus")]
    public async Task ExistingRegionsKeepCommandTypeAndDoNotAddSendVinHeader(string region)
    {
        var requests = new List<CapturedRequest>();
        using var h5 = new HttpClient(new RecordingHandler("h5", requests));
        using var app = new HttpClient(new RecordingHandler("app", requests));
        var client = new GwmApiClient(h5, app, NullLoggerFactory.Instance, region);
        var command = RemoteCommandFactory.CreateLockCommand(
            "VIN123",
            "0123456789abcdef0123456789abcdef",
            lockVehicle: true);

        await client.SendCmdAsync(command, CancellationToken.None);

        var send = Assert.Single(requests);
        Assert.Equal("app", send.ClientName);
        Assert.False(send.Headers.ContainsKey("vin"));
        using var json = JsonDocument.Parse(send.Body);
        Assert.Equal(2, json.RootElement.GetProperty("type").GetInt32());
    }

    private static HttpClient CreateSignedClient(
        string clientName,
        List<CapturedRequest> requests)
    {
        return new HttpClient(new BtAuthSigningHandler(BtAuthSigningHandler.Profiles.Rus)
        {
            InnerHandler = new RecordingHandler(clientName, requests)
        });
    }

    private static void AssertHeader(CapturedRequest request, string name, string expected)
    {
        Assert.True(request.Headers.TryGetValue(name, out var values));
        Assert.Equal(expected, Assert.Single(values!));
    }

    private sealed record CapturedRequest(
        string ClientName,
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string[]> Headers,
        string Body);

    private sealed class RecordingHandler(
        string clientName,
        List<CapturedRequest> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in request.Headers)
            {
                headers[name] = values.ToArray();
            }
            if (request.Content is not null)
            {
                foreach (var (name, values) in request.Content.Headers)
                {
                    headers[name] = values.ToArray();
                }
            }

            var body = request.Content is null
                ? String.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add(new CapturedRequest(
                clientName,
                request.Method,
                request.RequestUri!,
                headers,
                body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessResponse, Encoding.UTF8, "application/json")
            };
        }
    }
}
