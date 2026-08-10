using System.Net;
using System.Text;
using libgwmapi;
using libgwmapi.DTO.AppAuth;
using libgwmapi.DTO.UserAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace GwmOra.Addon.Tests;

public class GwmApiClientV2Tests
{
    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task V2LoginUsesDedicatedAuthenticationClient()
    {
        const string loginResponse = """
        {"code":"000000","description":"SUCCESS","data":{"accessToken":"a","refreshToken":"r","gwId":"g","beanId":"b"}}
        """;
        var h5Handler = new CapturingHandler(loginResponse);
        var authHandler = new CapturingHandler(loginResponse);
        var appHandler = new CapturingHandler(loginResponse);
        var certificateHandler = new CapturingHandler(loginResponse);
        using var h5 = new HttpClient(h5Handler);
        using var auth = new HttpClient(authHandler);
        using var app = new HttpClient(appHandler);
        using var certificate = new HttpClient(certificateHandler);
        var client = new GwmApiClient(
            h5,
            auth,
            app,
            certificate,
            NullLoggerFactory.Instance,
            "eu");

        await client.LoginWithPasswordV2Async(new EuLoginWithPasswordRequest
        {
            Account = "owner@example.com",
            CountryCode = "+49",
            Password = "secret",
            DeviceId = "0123456789abcdef",
            Country = "DE"
        }, CancellationToken.None);

        Assert.Equal(0, h5Handler.Calls);
        Assert.Equal(1, authHandler.Calls);
        Assert.Equal(
            "/app-api/api/v2.0/userAuth/loginWithPassword",
            authHandler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CertificateEnrollmentUsesCommonGatewayClient()
    {
        const string response = """
        {"code":"000000","description":"SUCCESS","data":{"encoded":"cert","notAfter":"20270807161120"}}
        """;
        var h5Handler = new CapturingHandler(response);
        var authHandler = new CapturingHandler(response);
        var appHandler = new CapturingHandler(response);
        var certificateHandler = new CapturingHandler(response);
        using var h5 = new HttpClient(h5Handler);
        using var auth = new HttpClient(authHandler);
        using var app = new HttpClient(appHandler);
        using var certificate = new HttpClient(certificateHandler);
        var client = new GwmApiClient(
            h5,
            auth,
            app,
            certificate,
            NullLoggerFactory.Instance,
            "eu");

        await client.ApplyCertificateAsync(new ApplyCertificateRequest
        {
            Csr = "csr",
            Phone = "123"
        }, CancellationToken.None);

        Assert.Equal(1, certificateHandler.Calls);
        Assert.Equal(
            "/app-api/api/v1.0/appAuth/applyCertificate",
            certificateHandler.Request!.RequestUri!.AbsolutePath);
    }
}
