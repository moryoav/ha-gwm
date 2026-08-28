using System.Net;
using System.Text;
using System.Text.Json;
using GwmOra.Addon.Configuration;
using GwmOra.Addon.Gwm;
using GwmOra.Addon.Supervisor;
using libgwmapi;
using libgwmapi.China;
using libgwmapi.DTO.China;
using libgwmapi.DTO.UserAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace GwmOra.Addon.Tests;

public class GwmAuthenticationServiceTests
{
    // Russia e-mail login: plaintext password, isEncrypt=false, and NO countryCode.
    // Sending countryCode="RU" makes the gateway treat account as a phone number.
    [Fact]
    public void RusLoginAccountRequestSerializesEmailWithoutCountryCode()
    {
        var request = GwmAuthenticationService.CreateRussianPasswordLoginRequest(
            new AddonOptions
            {
                Username = "owner@example.com",
                Password = "Abcd1234",
                Country = "RU",
                Region = "rus"
            },
            "abcdef0123456789abcdef0123456789");

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"password\":\"Abcd1234\"", json);
        Assert.Contains("\"isEncrypt\":false", json);
        Assert.Contains("\"country\":\"RU\"", json);
        Assert.DoesNotContain("countryCode", json);
        Assert.Contains("\"agreement\":[1,2,18,19]", json);
    }

    [Fact]
    public void RusVerificationLoginUsesRussianAgreementsAndTrimmedCode()
    {
        var request = GwmAuthenticationService.CreateRussianVerificationLoginRequest(
            new AddonOptions
            {
                Username = "owner@example.com",
                Password = "Abcd1234",
                VerificationCode = " 123456 ",
                Country = "RU",
                Region = "rus"
            },
            "abcdef0123456789abcdef0123456789");

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"smsCode\":\"123456\"", json);
        Assert.Contains("\"agreement\":[1,2,18,19]", json);
    }

    [Fact]
    public void RusVerificationLoginRequiresCode()
    {
        var options = new AddonOptions
        {
            Username = "owner@example.com",
            Password = "Abcd1234",
            Country = "RU",
            Region = "rus"
        };

        Assert.Throws<InvalidOperationException>(() =>
            GwmAuthenticationService.CreateRussianVerificationLoginRequest(options, "device"));
    }

    [Fact]
    public void LoginAccountRequestOmitsCountryCodeWhenNull()
    {
        var request = new LoginAccountRequest
        {
            Account = "owner@example.com",
            Password = "Abcd1234",
            Country = "RU",
            CountryCode = null!,
            IsEncrypt = false,
            DeviceId = "device",
            Model = "ha-gwm-ora",
            PushToken = String.Empty
        };

        var json = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("countryCode", json);
    }

    [Fact]
    public async Task ChinaRefreshFailurePersistsRotatedTokensWithoutReusingSmsCode()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"gwm-auth-{Guid.NewGuid():N}.json");
        try
        {
            var store = AddonStateStore.Load(statePath);
            await store.UpdateAsync(state => state.ChinaSession = new ChinaSession
            {
                GToken = "old-g-token",
                GRefreshToken = "old-g-refresh",
                SsoToken = "old-sso-token",
                UserId = "g-user",
                Phone = "13800138000"
            }, CancellationToken.None);

            var smsLoginCalls = 0;
            using var gApp = new HttpClient(new DelegateHandler(request =>
            {
                switch (request.RequestUri!.AbsolutePath)
                {
                    case "/api-guser/v5/token/refresh":
                        return Json("""
                            {"code":"000000","data":{"gToken":"new-g-token","gRefreshToken":"new-g-refresh","ssoToken":"new-sso-token"}}
                            """);
                    case "/tsp/v1/proxy/navinfo/GW.M.APP_LOGIN":
                        return Json("""
                            {"code":"000000","data":{"header":{"c":"500","m":"vehicle login unavailable"},"body":{}}}
                            """);
                    case "/api-guser/v5/user/sms-login":
                        smsLoginCalls++;
                        return Json("{\"code\":\"7000000\",\"description\":\"expired\"}");
                    default:
                        throw new Xunit.Sdk.XunitException(
                            $"Unexpected G-App request {request.RequestUri.AbsolutePath}");
                }
            }));
            using var beanTech = new HttpClient(new DelegateHandler(_ => Json("""
                {"code":"000000","data":{"accessToken":"bt-access","refreshToken":"bt-refresh","beanId":"bt-bean"}}
                """)));
            using var unusedCar = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("Car service should not be called")));
            using var unusedAutoAi = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("Direct AutoAI service should not be called")));
            var protocol = new ChinaProtocolClient(
                gApp,
                unusedCar,
                beanTech,
                unusedAutoAi,
                NullLoggerFactory.Instance)
            {
                DeviceId = "0123456789abcdef0123456789abcdef"
            };
            var client = new GwmApiClient(protocol, NullLoggerFactory.Instance);
            var service = CreateChinaAuthenticationService(store, "654321");

            var exception = await Assert.ThrowsAsync<GwmVerificationRequiredException>(() =>
                service.EnsureAuthenticatedAsync(client, CancellationToken.None));

            Assert.Contains("refreshed partial session was saved", exception.Message);
            Assert.Equal(0, smsLoginCalls);
            Assert.Equal("new-g-token", store.State.ChinaSession!.GToken);
            Assert.Equal("new-g-refresh", store.State.ChinaSession.GRefreshToken);
            Assert.Equal("new-sso-token", store.State.ChinaSession.SsoToken);
        }
        finally
        {
            File.Delete(statePath);
            File.Delete(statePath + ".tmp");
        }
    }

    [Fact]
    public async Task ChinaSmsCodeIsSubmittedOnlyOncePerAddonProcess()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"gwm-auth-{Guid.NewGuid():N}.json");
        try
        {
            var store = AddonStateStore.Load(statePath);
            var smsLoginCalls = 0;
            var smsRequestCalls = 0;
            using var gApp = new HttpClient(new DelegateHandler(request =>
            {
                switch (request.RequestUri!.AbsolutePath)
                {
                    case "/api-guser/v5/user/sms-login":
                        smsLoginCalls++;
                        return Json("{\"code\":\"7000000\",\"description\":\"expired\"}");
                    case "/api-guser/v5/user/login-sms/send":
                        smsRequestCalls++;
                        return Json("{\"code\":\"000000\",\"data\":{}}");
                    default:
                        throw new Xunit.Sdk.XunitException(
                            $"Unexpected G-App request {request.RequestUri.AbsolutePath}");
                }
            }));
            using var unusedCar = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("Car service should not be called")));
            using var unusedBeanTech = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("BeanTech should not be called")));
            using var unusedAutoAi = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("AutoAI should not be called")));
            var protocol = new ChinaProtocolClient(
                gApp,
                unusedCar,
                unusedBeanTech,
                unusedAutoAi,
                NullLoggerFactory.Instance)
            {
                DeviceId = "0123456789abcdef0123456789abcdef"
            };
            var client = new GwmApiClient(protocol, NullLoggerFactory.Instance);
            var service = CreateChinaAuthenticationService(store, "654321");

            await Assert.ThrowsAsync<GwmVerificationRequiredException>(() =>
                service.EnsureAuthenticatedAsync(client, CancellationToken.None));
            await Assert.ThrowsAsync<GwmVerificationRequiredException>(() =>
                service.EnsureAuthenticatedAsync(client, CancellationToken.None));

            Assert.Equal(1, smsLoginCalls);
            Assert.Equal(1, smsRequestCalls);
        }
        finally
        {
            File.Delete(statePath);
            File.Delete(statePath + ".tmp");
        }
    }

    [Fact]
    public async Task ChinaAccountChangeClearsStoredSessionAndRequestsFreshSms()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"gwm-auth-{Guid.NewGuid():N}.json");
        try
        {
            var store = AddonStateStore.Load(statePath);
            await store.UpdateAsync(state =>
            {
                state.AccessToken = "overseas-access";
                state.RefreshToken = "overseas-refresh";
                state.GwId = "old-gw";
                state.BeanId = "old-bean";
                state.ClientCertificate = "old-certificate";
                state.ClientPrivateKey = "old-private-key";
                state.VerificationCodeRequestedAt = DateTimeOffset.UtcNow;
                state.ChinaSession = new ChinaSession
                {
                    GToken = "old-g-token",
                    GRefreshToken = "old-g-refresh",
                    UserId = "old-user",
                    Phone = "13800138000",
                    BeanTechAccessToken = "old-bt-token",
                    AutoAiTokenId = "old-auto-token",
                    AutoAiUserId = "old-auto-user"
                };
                state.ChargingPlansSetByAddon["old-vehicle"] = new TrackedChargingPlan();
            }, CancellationToken.None);

            var smsRequestCalls = 0;
            using var gApp = new HttpClient(new DelegateHandler(request =>
            {
                Assert.Equal("/api-guser/v5/user/login-sms/send", request.RequestUri!.AbsolutePath);
                smsRequestCalls++;
                return Json("{\"code\":\"000000\",\"data\":{}}");
            }));
            using var unusedCar = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("Car service should not be called")));
            using var unusedBeanTech = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("BeanTech should not be called")));
            using var unusedAutoAi = new HttpClient(new DelegateHandler(_ =>
                throw new Xunit.Sdk.XunitException("AutoAI should not be called")));
            var protocol = new ChinaProtocolClient(
                gApp,
                unusedCar,
                unusedBeanTech,
                unusedAutoAi,
                NullLoggerFactory.Instance)
            {
                DeviceId = "0123456789abcdef0123456789abcdef"
            };
            var client = new GwmApiClient(protocol, NullLoggerFactory.Instance);
            var service = CreateChinaAuthenticationService(
                store,
                verificationCode: null,
                username: "13900139000");

            var exception = await Assert.ThrowsAsync<GwmVerificationRequiredException>(() =>
                service.EnsureAuthenticatedAsync(client, CancellationToken.None));

            Assert.Contains("verification code was requested", exception.Message);
            Assert.Equal(1, smsRequestCalls);
            Assert.Null(store.State.ChinaSession);
            Assert.Null(store.State.AccessToken);
            Assert.Null(store.State.RefreshToken);
            Assert.Null(store.State.GwId);
            Assert.Null(store.State.BeanId);
            Assert.Null(store.State.ClientCertificate);
            Assert.Null(store.State.ClientPrivateKey);
            Assert.NotNull(store.State.VerificationCodeRequestedAt);
            Assert.Empty(store.State.ChargingPlansSetByAddon);
            Assert.False(String.IsNullOrWhiteSpace(store.State.AuthenticationContextFingerprint));
            Assert.DoesNotContain("13900139000", store.State.AuthenticationContextFingerprint!);
        }
        finally
        {
            File.Delete(statePath);
            File.Delete(statePath + ".tmp");
        }
    }

    [Fact]
    public void AuthenticationContextTracksOnlyLoginIdentity()
    {
        var original = new AddonOptions
        {
            Region = "eu",
            Country = "DE",
            Username = "owner@example.com",
            Password = "old-password",
            VerificationCode = "111111",
            SecurityPin = "1234",
            EnableRemoteCommands = false,
            EnableChargingControl = false,
            PollIntervalSeconds = 60
        };
        var operationalChange = new AddonOptions
        {
            Region = "eu",
            Country = "DE",
            Username = "owner@example.com",
            Password = "old-password",
            VerificationCode = "222222",
            SecurityPin = "5678",
            EnableRemoteCommands = true,
            EnableChargingControl = true,
            PollIntervalSeconds = 300
        };

        Assert.Equal(
            GwmAuthenticationService.AuthenticationContextFingerprint(original),
            GwmAuthenticationService.AuthenticationContextFingerprint(operationalChange));
        Assert.NotEqual(
            GwmAuthenticationService.AuthenticationContextFingerprint(original),
            GwmAuthenticationService.AuthenticationContextFingerprint(
                new AddonOptions
                {
                    Region = "eu",
                    Country = "DE",
                    Username = "different@example.com",
                    Password = "old-password"
                }));
        Assert.NotEqual(
            GwmAuthenticationService.AuthenticationContextFingerprint(original),
            GwmAuthenticationService.AuthenticationContextFingerprint(
                new AddonOptions
                {
                    Region = "eu",
                    Country = "DE",
                    Username = "owner@example.com",
                    Password = "new-password"
                }));
    }

    private static GwmAuthenticationService CreateChinaAuthenticationService(
        AddonStateStore store,
        string? verificationCode,
        string username = "13800138000") =>
        new(
            new AddonOptions
            {
                Region = "cn",
                Country = "CN",
                Username = username,
                VerificationCode = verificationCode
            },
            store,
            new SupervisorOptionsService(NullLogger<SupervisorOptionsService>.Instance),
            NullLogger<GwmAuthenticationService>.Instance);

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
