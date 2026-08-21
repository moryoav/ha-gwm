using System.Text.Json;
using GwmOra.Addon.Configuration;
using GwmOra.Addon.Gwm;
using libgwmapi.DTO.UserAuth;

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
}
