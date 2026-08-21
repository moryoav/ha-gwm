using libgwmapi;

namespace GwmOra.Addon.Tests;

public class BtAuthSigningHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private static async Task<HttpRequestMessage> SendAsync(
        HttpMethod method,
        string url,
        HttpContent? content = null,
        BtAuthSigningHandler? signingHandler = null)
    {
        var capture = new CapturingHandler();
        var handler = signingHandler ?? new BtAuthSigningHandler();
        handler.InnerHandler = capture;
        using var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(method, url) { Content = content };
        await invoker.SendAsync(request, CancellationToken.None);
        return capture.Captured!;
    }

    private const string Base = "https://aus-h5-gateway.gwmcloud.com/app-api/api/v1.0/";

    [Fact]
    public async Task GetDropsEmptyQueryParamsFromUrl()
    {
        // The empty seqNo= is what the gateway rejected with 607099; it must be dropped.
        using var sent = await SendAsync(HttpMethod.Get, Base + "vehicle/getLastStatus?vin=ABC&seqNo=");

        Assert.Equal("?vin=ABC", sent.RequestUri!.Query);
    }

    [Fact]
    public async Task GetSortsNonEmptyQueryParams()
    {
        using var sent = await SendAsync(HttpMethod.Get, Base + "vehicle/vehicleBasicsInfo?vin=ABC&flag=true");

        Assert.Equal("?flag=true&vin=ABC", sent.RequestUri!.Query);
    }

    [Fact]
    public async Task AttachesBtAuthHeaders()
    {
        using var sent = await SendAsync(HttpMethod.Get, Base + "user/getUserBaseInfo");

        Assert.True(sent.Headers.Contains("bt-auth-sign"));
        Assert.True(sent.Headers.Contains("bt-auth-appkey"));
        Assert.True(sent.Headers.Contains("bt-auth-nonce"));
        Assert.True(sent.Headers.Contains("bt-auth-timestamp"));
    }

    [Fact]
    public async Task PostLeavesTheUrlUntouched()
    {
        using var sent = await SendAsync(HttpMethod.Post, Base + "userAuth/loginAccount",
            new StringContent("{\"account\":\"a\"}"));

        Assert.Equal("/app-api/api/v1.0/userAuth/loginAccount", sent.RequestUri!.AbsolutePath);
        Assert.Equal(string.Empty, sent.RequestUri!.Query);
        Assert.True(sent.Headers.Contains("bt-auth-sign"));
    }

    [Fact]
    public async Task GetSignatureMatchesKnownVector()
    {
        var handler = new BtAuthSigningHandler(
            () => "1721462400123",
            () => "0123456789abcdef");

        using var sent = await SendAsync(
            HttpMethod.Get,
            Base + "vehicle/getLastStatus?vin=ABC&seqNo=",
            signingHandler: handler);

        Assert.Equal("1721462400123", sent.Headers.GetValues("bt-auth-timestamp").Single());
        Assert.Equal("0123456789abcdef", sent.Headers.GetValues("bt-auth-nonce").Single());
        Assert.Equal(
            "5b2e9804a106e7d9dd7bacaee1a99c3a9bd23b4904840caa96d387cf17f24c05",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task MultiParameterGetSignatureMatchesCanonicalVector()
    {
        var handler = new BtAuthSigningHandler(
            () => "1721462400123",
            () => "0123456789abcdef");

        using var sent = await SendAsync(
            HttpMethod.Get,
            Base + "vehicle/vehicleBasicsInfo?vin=ABC&flag=true",
            signingHandler: handler);

        Assert.Equal("?flag=true&vin=ABC", sent.RequestUri!.Query);
        Assert.Equal(
            "79e40a17b69ec1023c30774b7b2ffa583f6e5c0718de8597289f222a73bc3797",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task GetSignatureLowercasesKeysWithoutChangingOutgoingQuery()
    {
        var handler = new BtAuthSigningHandler(
            () => "1721462400123",
            () => "0123456789abcdef");

        using var sent = await SendAsync(
            HttpMethod.Get,
            Base + "vehicle/getRemoteCtrlResultT5?seqNo=ABC123",
            signingHandler: handler);

        Assert.Equal("?seqNo=ABC123", sent.RequestUri!.Query);
        Assert.Equal(
            "42b6afb5a71da918b9cc4863a4b867d1b079c49abee6cb89d389d179486f4a63",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task PostSignatureMatchesKnownVector()
    {
        var handler = new BtAuthSigningHandler(
            () => "1721462400123",
            () => "0123456789abcdef");
        using var body = new StringContent("{\"account\":\"owner@example.com\",\"password\":\"secret\"}");

        using var sent = await SendAsync(
            HttpMethod.Post,
            Base + "userAuth/loginAccount",
            body,
            handler);

        Assert.Equal(
            "29245b8fa2a982d774f547775393019b7c351e0a75dd91a2fec0696268fc929b",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task RusAttachesGwmAuthHeadersWithApkCredentials()
    {
        var handler = new BtAuthSigningHandler(
            BtAuthSigningHandler.Profiles.Rus,
            () => "1721462400123",
            () => "0123456789abcdef");
        using var body = new StringContent("{\"account\":\"owner@example.com\",\"password\":\"secret\"}");

        using var sent = await SendAsync(
            HttpMethod.Post,
            "https://rus-h5-gateway.gwmcloud.com/app-api/api/v1.0/userAuth/loginAccount",
            body,
            handler);

        Assert.Equal("4694605273", sent.Headers.GetValues("gwm-auth-appkey").Single());
        Assert.Equal("1721462400123", sent.Headers.GetValues("gwm-auth-timestamp").Single());
        Assert.Equal("0123456789abcdef", sent.Headers.GetValues("gwm-auth-nonce").Single());
        Assert.Equal(
            "b2884756e4e1e7bd4137929a1bdd7243f360ce666822e9ac0d7d13fe67452e2b",
            sent.Headers.GetValues("gwm-auth-sign").Single());
        Assert.False(sent.Headers.Contains("bt-auth-sign"));
    }

    [Fact]
    public async Task RusGetSignatureMatchesKnownVector()
    {
        var handler = new BtAuthSigningHandler(
            BtAuthSigningHandler.Profiles.Rus,
            () => "1721462400123",
            () => "0123456789abcdef");

        using var sent = await SendAsync(
            HttpMethod.Get,
            "https://rus-h5-gateway.gwmcloud.com/app-api/api/v1.0/vehicle/getLastStatus?vin=ABC&seqNo=",
            signingHandler: handler);

        Assert.Equal("?vin=ABC", sent.RequestUri!.Query);
        Assert.Equal(
            "20129a1e63495078ff01094752dd2dadcce144778e4fbb1358f25470d8934edd",
            sent.Headers.GetValues("gwm-auth-sign").Single());
    }
}
