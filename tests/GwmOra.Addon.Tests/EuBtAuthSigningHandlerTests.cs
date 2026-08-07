using libgwmapi;

namespace GwmOra.Addon.Tests;

public class EuBtAuthSigningHandlerTests
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

    [Fact]
    public async Task CapturedAcquireVehiclesVectorMatchesCurrentGwmApp()
    {
        var capture = new CapturingHandler();
        var signer = new EuBtAuthSigningHandler(
            () => "1786119081976",
            () => "810FF2B7B31516FD")
        {
            InnerHandler = capture
        };
        using var invoker = new HttpMessageInvoker(signer);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://eu-app-gateway.gwmcloud.com/app-api/api/v1.0/globalapp/vehicle/acquireVehicles");

        await invoker.SendAsync(request, CancellationToken.None);

        var sent = capture.Captured!;
        Assert.Equal("1874226830", sent.Headers.GetValues("bt-auth-appkey").Single());
        Assert.Equal("810FF2B7B31516FD", sent.Headers.GetValues("bt-auth-nonce").Single());
        Assert.Equal(
            "44eb1744ae2a0d162ca84cd9a99b8de6e2664772afbd48662291d45fa9aeb506",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task EmptyQueryParametersAreRemovedBeforeSigning()
    {
        var capture = new CapturingHandler();
        var signer = new EuBtAuthSigningHandler(
            () => "1786119083388",
            () => "AFFA92EB363A983E")
        {
            InnerHandler = capture
        };
        using var invoker = new HttpMessageInvoker(signer);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://eu-app-gateway.gwmcloud.com/app-api/api/v1.0/vehicle/getLastStatus?vin=ABC&seqNo=&modelId=");

        await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal("?vin=ABC", capture.Captured!.RequestUri!.Query);
    }
}
