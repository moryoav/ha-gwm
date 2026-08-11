using libgwmapi;

namespace GwmOra.Addon.Tests;

public class GwmAuthSigningHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
        HttpContent? content,
        GwmAuthSigningHandler signingHandler)
    {
        var capture = new CapturingHandler();
        signingHandler.InnerHandler = capture;
        using var invoker = new HttpMessageInvoker(signingHandler);
        var request = new HttpRequestMessage(method, url) { Content = content };
        await invoker.SendAsync(request, CancellationToken.None);
        return capture.Captured!;
    }

    [Fact]
    public async Task CapturedGetVectorMatchesCurrentGwmApp()
    {
        var handler = new GwmAuthSigningHandler(
            () => "1786109613161",
            () => "e9f40a6b66f5f765");

        using var sent = await SendAsync(
            HttpMethod.Get,
            "https://eu-h5-gateway.gwmcloud.com/app-api/api/v1.0/complaintsComments/appInitConfig",
            null,
            handler);

        Assert.Equal("1874226830", sent.Headers.GetValues("gwm-auth-appkey").Single());
        Assert.Equal("1786109613161", sent.Headers.GetValues("gwm-auth-timestamp").Single());
        Assert.Equal("e9f40a6b66f5f765", sent.Headers.GetValues("gwm-auth-nonce").Single());
        Assert.Equal(
            "0f01647b577c905cd442476d771106ca37baf60db389bc76f7c8994a02f22c36",
            sent.Headers.GetValues("gwm-auth-sign").Single());
    }

    [Fact]
    public async Task SyntheticPostVectorMatchesCanonicalAlgorithm()
    {
        var handler = new GwmAuthSigningHandler(
            () => "1721462400123",
            () => "0123456789abcdef");
        using var body = new StringContent(
            "{\"account\":\"owner@example.com\",\"password\":\"secret\"}");

        using var sent = await SendAsync(
            HttpMethod.Post,
            "https://eu-h5-gateway.gwmcloud.com/app-api/api/v2.0/userAuth/loginWithPassword",
            body,
            handler);

        Assert.Equal(
            "b58c8529abf47969497b2486169d024858c77735074fd3c7ed299226c1b08ab2",
            sent.Headers.GetValues("gwm-auth-sign").Single());
    }
}
