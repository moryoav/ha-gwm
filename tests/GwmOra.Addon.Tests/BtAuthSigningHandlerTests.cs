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

    private static async Task<HttpRequestMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null)
    {
        var capture = new CapturingHandler();
        var handler = new BtAuthSigningHandler { InnerHandler = capture };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(method, url) { Content = content };
        await invoker.SendAsync(request, CancellationToken.None);
        return capture.Captured!;
    }

    private const string Base = "https://aus-h5-gateway.gwmcloud.com/app-api/api/v1.0/";

    [Fact]
    public async Task GetDropsEmptyQueryParamsFromUrl()
    {
        // The empty seqNo= is what the gateway rejected with 607099; it must be dropped.
        var sent = await SendAsync(HttpMethod.Get, Base + "vehicle/getLastStatus?vin=ABC&seqNo=");

        Assert.Equal("?vin=ABC", sent.RequestUri!.Query);
    }

    [Fact]
    public async Task GetSortsNonEmptyQueryParams()
    {
        var sent = await SendAsync(HttpMethod.Get, Base + "vehicle/vehicleBasicsInfo?vin=ABC&flag=true");

        Assert.Equal("?flag=true&vin=ABC", sent.RequestUri!.Query);
    }

    [Fact]
    public async Task AttachesBtAuthHeaders()
    {
        var sent = await SendAsync(HttpMethod.Get, Base + "user/getUserBaseInfo");

        Assert.True(sent.Headers.Contains("bt-auth-sign"));
        Assert.True(sent.Headers.Contains("bt-auth-appkey"));
        Assert.True(sent.Headers.Contains("bt-auth-nonce"));
        Assert.True(sent.Headers.Contains("bt-auth-timestamp"));
    }

    [Fact]
    public async Task PostLeavesTheUrlUntouched()
    {
        var sent = await SendAsync(HttpMethod.Post, Base + "userAuth/loginAccount",
            new StringContent("{\"account\":\"a\"}"));

        Assert.Equal("/app-api/api/v1.0/userAuth/loginAccount", sent.RequestUri!.AbsolutePath);
        Assert.Equal(string.Empty, sent.RequestUri!.Query);
        Assert.True(sent.Headers.Contains("bt-auth-sign"));
    }
}
