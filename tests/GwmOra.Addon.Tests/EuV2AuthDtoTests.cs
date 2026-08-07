using System.Text.Json;
using libgwmapi.DTO.UserAuth;

namespace GwmOra.Addon.Tests;

public class EuV2AuthDtoTests
{
    [Fact]
    public void LoginRequestMatchesCapturedV2PayloadShape()
    {
        var request = new EuLoginWithPasswordRequest
        {
            Account = "owner@example.com",
            CountryCode = "+972",
            Password = "secret",
            DeviceId = "0123456789abcdef",
            Country = "IL",
            VerifyCode = "4873",
            ValidCodeMode = "1"
        };

        var json = JsonSerializer.Serialize(request);

        Assert.Equal(
            "{\"account\":\"owner@example.com\",\"accountType\":\"2\",\"countryCode\":\"+972\",\"agreement\":[1,2],\"password\":\"secret\",\"deviceId\":\"0123456789abcdef\",\"appType\":\"0\",\"pushToken\":\"\",\"country\":\"IL\",\"verifyCode\":\"4873\",\"validCodeMode\":\"1\"}",
            json);
    }

    [Fact]
    public void LoginRequestOmitsVerificationFieldsBeforeChallenge()
    {
        var request = new EuLoginWithPasswordRequest
        {
            Account = "owner@example.com",
            CountryCode = "+49",
            Password = "secret",
            DeviceId = "0123456789abcdef",
            Country = "DE"
        };

        var json = JsonSerializer.Serialize(request);

        Assert.DoesNotContain("verifyCode", json);
        Assert.DoesNotContain("validCodeMode", json);
    }
}
