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

    private static async Task<HttpRequestMessage> SendAsync(
        HttpMethod method,
        string url,
        EuBtAuthSigningHandler signingHandler,
        HttpContent? content = null)
    {
        var capture = new CapturingHandler();
        signingHandler.InnerHandler = capture;
        using var invoker = new HttpMessageInvoker(signingHandler);
        using var request = new HttpRequestMessage(method, url) { Content = content };
        await invoker.SendAsync(request, CancellationToken.None);
        return capture.Captured!;
    }

    [Fact]
    public async Task CapturedAcquireVehiclesVectorMatchesCurrentGwmApp()
    {
        var signer = new EuBtAuthSigningHandler(
            () => "1786119081976",
            () => "810FF2B7B31516FD");

        using var sent = await SendAsync(
            HttpMethod.Get,
            "https://eu-app-gateway.gwmcloud.com/app-api/api/v1.0/globalapp/vehicle/acquireVehicles",
            signer);

        Assert.Equal("1874226830", sent.Headers.GetValues("bt-auth-appkey").Single());
        Assert.Equal("810FF2B7B31516FD", sent.Headers.GetValues("bt-auth-nonce").Single());
        Assert.Equal(
            "44eb1744ae2a0d162ca84cd9a99b8de6e2664772afbd48662291d45fa9aeb506",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task CapturedGetLastStatusVectorKeepsAndSignsEmptyParameters()
    {
        const string vin =
            "364b543434447861582f66744a743231636d64577035716b727a346863424a5344475458585045314343733d";
        var signer = new EuBtAuthSigningHandler(
            () => "1786119094170",
            () => "38AA045BAECB0A9B");
        var url =
            $"https://eu-app-gateway.gwmcloud.com/app-api/api/v1.0/vehicle/getLastStatus" +
            $"?vin={vin}&seqNo=&modelId=";

        using var sent = await SendAsync(HttpMethod.Get, url, signer);

        Assert.Equal($"?vin={vin}&seqNo=&modelId=", sent.RequestUri!.Query);
        Assert.Equal(
            "3a96df99215e51f70218b6e8a0004d26174a5f727715d371d225615d50e7b081",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task CapturedMultiParameterVectorSortsAndLowercasesKeys()
    {
        var signer = new EuBtAuthSigningHandler(
            () => "1786109619155",
            () => "311FD65FFE955342");

        using var sent = await SendAsync(
            HttpMethod.Get,
            "https://eu-h5-gateway.gwmcloud.com/app-api/api/v1.0/complaintsComments/findLastVersion" +
            "?type=Android&versionNum=1.3.0",
            signer);

        Assert.Equal("?type=Android&versionNum=1.3.0", sent.RequestUri!.Query);
        Assert.Equal(
            "32eb4ab7b917c478867c128a327dc6d690690307decd1219406fcd9738e2847f",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task QueryValuesAreUrlDecodedForSigningWithoutChangingTheUrl()
    {
        var signer = new EuBtAuthSigningHandler(
            () => "1721462400123",
            () => "0123456789ABCDEF");
        const string query = "?z=hello%20world&empty=&A=One%2FTwo";

        using var sent = await SendAsync(
            HttpMethod.Get,
            "https://eu-app-gateway.gwmcloud.com/app-api/api/v1.0/vehicle/test" + query,
            signer);

        Assert.Equal(query, sent.RequestUri!.Query);
        Assert.Equal(
            "d43ac09bf68b7ad87be78a756c43b96616b0aa4cb0d7a1fffd65c768d8834f76",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }

    [Fact]
    public async Task PostSignsTheExactJsonBody()
    {
        var signer = new EuBtAuthSigningHandler(
            () => "1721462400123",
            () => "0123456789ABCDEF");
        using var body = new StringContent("{\"csr\":\"ABC\",\"phone\":\"123\"}");

        using var sent = await SendAsync(
            HttpMethod.Post,
            "https://eu-app-gateway-common.gwmcloud.com/app-api/api/v1.0/appAuth/applyCertificate",
            signer,
            body);

        Assert.Equal(
            "67ce44d071cac2ee24d3e3a3b9eeb3ebd24149e461078fa420412b4d83db4971",
            sent.Headers.GetValues("bt-auth-sign").Single());
    }
}
